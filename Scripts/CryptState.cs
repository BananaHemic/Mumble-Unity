using System;
using System.Security.Cryptography;
using MumbleProto;
using UnityEngine;

namespace Mumble
{
    public class CryptState
    {
        private static readonly int AES_BLOCK_SIZE = 16;
        private readonly byte[] _decryptHistory = new byte[256];

        private CryptSetup _cryptSetup;
        private ICryptoTransform _decryptor;
        private ICryptoTransform _encryptor;

        private int _good;
        private int _late;
        private int _lost;

        // Used by Decrypt
        private readonly byte[] saveiv = new byte[AES_BLOCK_SIZE];
        private readonly byte[] tag = new byte[AES_BLOCK_SIZE];
        // Used by OCB Decrypt
        private readonly byte[] checksum = new byte[AES_BLOCK_SIZE];
        private readonly byte[] tmp = new byte[AES_BLOCK_SIZE];
        private readonly byte[] delta = new byte[AES_BLOCK_SIZE];
        private readonly byte[] pad = new byte[AES_BLOCK_SIZE];

        public CryptSetup CryptSetup
        {
            get { return _cryptSetup; }
            set
            {
                _cryptSetup = value;
                var aesAlg = new AesManaged
                {
                    BlockSize = AES_BLOCK_SIZE*8,
                    Key = _cryptSetup.Key,
                    Mode = CipherMode.ECB,
                    Padding = PaddingMode.None
                };
                _encryptor = aesAlg.CreateEncryptor();
                _decryptor = aesAlg.CreateDecryptor();
            }
        }

        private void S2(byte[] block)
        {
            int carry = (block[0] >> 7) & 0x1;
            for (int i = 0; i < AES_BLOCK_SIZE - 1; i++)
            {
                block[i] = (byte) ((block[i] << 1) | ((block[i + 1] >> 7) & 0x1));
            }
            block[AES_BLOCK_SIZE - 1] = (byte) ((block[AES_BLOCK_SIZE - 1] << 1) ^ (carry*0x87));
        }

        private void S3(byte[] block)
        {
            int carry = (block[0] >> 7) & 0x1;
            for (int i = 0; i < AES_BLOCK_SIZE - 1; i++)
            {
                block[i] ^= (byte) ((block[i] << 1) | ((block[i + 1] >> 7) & 0x1));
            }
            block[AES_BLOCK_SIZE - 1] ^= (byte) ((block[AES_BLOCK_SIZE - 1] << 1) ^ (carry*0x87));
        }

        private void Xor(byte[] dst, byte[] a, byte[] b)
        {
            for (int i = 0; i < AES_BLOCK_SIZE; i++)
            {
                dst[i] = (byte) (a[i] ^ b[i]);
            }
        }
        private void Xor(byte[] dst, byte[] a, byte[] b, int dst_offset, int a_offset, int b_offset)
        {
            
            for (int i = 0; i < AES_BLOCK_SIZE; i++)
            {
                dst[dst_offset + i] = (byte) (a[a_offset + i] ^ b[b_offset + i]);
            }
        }

        private void ZERO(byte[] block)
        {
            Array.Clear(block, 0, block.Length);
        }

        // buffer + amount of useful bytes in buffer
        public byte[] Encrypt(byte[] inBytes, int length)
        {
            for (int i = 0; i < AES_BLOCK_SIZE; i++)
            {
                if (++_cryptSetup.ClientNonce[i] != 0)
                    break;
            }

//            _logger.Debug("Encrypting " + length + " bytes");
            var tag = new byte[AES_BLOCK_SIZE];

            var dst = new byte[length];
            OcbEncrypt(inBytes, length, dst, _cryptSetup.ClientNonce, tag);

            var fdst = new byte[dst.Length + 4];
//            _logger.Debug("IV: " + (int) _cryptSetup.ClientNonce[0]);
            fdst[0] = _cryptSetup.ClientNonce[0];
            fdst[1] = tag[0];
            fdst[2] = tag[1];
            fdst[3] = tag[2];

            dst.CopyTo(fdst, 4);
            return fdst;
        }

        private void OcbEncrypt(byte[] plain, int plainLength, byte[] encrypted, byte[] nonce, byte[] tag)
        {
            var checksum = new byte[AES_BLOCK_SIZE];
            var tmp = new byte[AES_BLOCK_SIZE];

//            byte[] delta = encryptCipher.doFinal(nonce);
            var delta = new byte[AES_BLOCK_SIZE];
            _encryptor.TransformBlock(nonce, 0, AES_BLOCK_SIZE, delta, 0);

            int offset = 0;
            int len = plainLength;
            while (len > AES_BLOCK_SIZE)
            {
                var buffer = new byte[AES_BLOCK_SIZE];
                S2(delta);
                Array.Copy(plain, offset, buffer, 0, AES_BLOCK_SIZE);
                Xor(checksum, checksum, buffer);
                Xor(tmp, delta, buffer);

//                encryptCipher.doFinal(tmp, 0, AES_BLOCK_SIZE, tmp);
                _encryptor.TransformBlock(tmp, 0, AES_BLOCK_SIZE, tmp, 0);

                Xor(buffer, delta, tmp);
                Array.Copy(buffer, 0, encrypted, offset, AES_BLOCK_SIZE);
                offset += AES_BLOCK_SIZE;
                len -= AES_BLOCK_SIZE;
            }

            S2(delta);
            ZERO(tmp);
            long num = len*8;
            tmp[AES_BLOCK_SIZE - 2] = (byte) ((num >> 8) & 0xFF);
            tmp[AES_BLOCK_SIZE - 1] = (byte) (num & 0xFF);
            Xor(tmp, tmp, delta);

//            byte[] pad = encryptCipher.doFinal(tmp);
            var pad = new byte[AES_BLOCK_SIZE];
            _encryptor.TransformBlock(tmp, 0, AES_BLOCK_SIZE, pad, 0);

            Array.Copy(plain, offset, tmp, 0, len);
            Array.Copy(pad, len, tmp, len, AES_BLOCK_SIZE - len);

            Xor(checksum, checksum, tmp);
            Xor(tmp, pad, tmp);
            Array.Copy(tmp, 0, encrypted, offset, len);

            S3(delta);
            Xor(tmp, delta, checksum);

//            encryptCipher.doFinal(tmp, 0, AES_BLOCK_SIZE, tag);
            _encryptor.TransformBlock(tmp, 0, AES_BLOCK_SIZE, tag, 0);
        }

        public byte[] Decrypt(byte[] source, int length)
        {
            if (length < 4)
            {
                Debug.LogError("Length less than 4, decryption failed");
                return null;
            }

            byte ivbyte = source[0];
            bool restore = false;

            int lost = 0;
            int late = 0;

            Array.Copy(_cryptSetup.ServerNonce, 0, saveiv, 0, AES_BLOCK_SIZE);

            if (((_cryptSetup.ServerNonce[0] + 1) & 0xFF) == ivbyte)
            {
                // In order as expected.
                if (ivbyte > _cryptSetup.ServerNonce[0])
                {
                    _cryptSetup.ServerNonce[0] = ivbyte;
                }
                else if (ivbyte < _cryptSetup.ServerNonce[0])
                {
                    _cryptSetup.ServerNonce[0] = ivbyte;
                    for (int i = 1; i < AES_BLOCK_SIZE; i++)
                    {
                        if ((++_cryptSetup.ServerNonce[i]) != 0)
                            break;
                    }
                }
                else
                {
                    Debug.LogError("Crypt: 1");
                    return null;
                }
            }
            else
            {
                // This is either out of order or a repeat.
                int diff = ivbyte - _cryptSetup.ServerNonce[0];
                if (diff > 128)
                {
                    diff = diff - 256;
                }
                else if (diff < -128)
                {
                    diff = diff + 256;
                }

                if ((ivbyte < _cryptSetup.ServerNonce[0]) && (diff > -30) && (diff < 0))
                {
                    // Late packet, but no wraparound.
                    late = 1;
                    lost = -1;
                    _cryptSetup.ServerNonce[0] = ivbyte;
                    restore = true;
                }
                else if ((ivbyte > _cryptSetup.ServerNonce[0]) && (diff > -30) &&
                         (diff < 0))
                {
                    // Last was 0x02, here comes 0xff from last round
                    late = 1;
                    lost = -1;
                    _cryptSetup.ServerNonce[0] = ivbyte;
                    for (int i = 1; i < AES_BLOCK_SIZE; i++)
                    {
                        if ((_cryptSetup.ServerNonce[i]--) != 0)
                            break;
                    }
                    restore = true;
                }
                else if ((ivbyte > _cryptSetup.ServerNonce[0]) && (diff > 0))
                {
                    // Lost a few packets, but beyond that we're good.
                    lost = ivbyte - _cryptSetup.ServerNonce[0] - 1;
                    _cryptSetup.ServerNonce[0] = ivbyte;
                }
                else if ((ivbyte < _cryptSetup.ServerNonce[0]) && (diff > 0))
                {
                    // Lost a few packets, and wrapped around
                    lost = 256 - (_cryptSetup.ServerNonce[0] & 0xFF) + ivbyte - 1;
                    _cryptSetup.ServerNonce[0] = ivbyte;
                    for (int i = 1; i < AES_BLOCK_SIZE; i++)
                    {
                        if ((++_cryptSetup.ServerNonce[i]) != 0)
                            break;
                    }
                }
                else
                {
                    // Happens if the packets arrive out of order
                    Debug.LogError("Crypt: 2");
                    return null;
                }

                //TODO should ClientNonce end in 0?
                if (_decryptHistory[_cryptSetup.ServerNonce[0]] == _cryptSetup.ClientNonce[1])
                {
                    Array.Copy(saveiv, 0, _cryptSetup.ServerNonce, 0, AES_BLOCK_SIZE);
                    Debug.LogError("Crypt: 3");
                    return null;
                }
            }

            int plainLength = length - 4;
            var dst = new byte[plainLength];
            OcbDecrypt(source, plainLength, dst, _cryptSetup.ServerNonce, tag, 4);

            if (tag[0] != source[1]
                || tag[1] != source[2]
                || tag[2] != source[3])
            {
                Debug.Log(tag[0] + " " + source[1] + "\n"
                    + tag[1] + " " + source[2] + "\n"
                    + tag[2] + " " + source[3]
                    );

                Array.Copy(saveiv, 0, _cryptSetup.ServerNonce, 0, AES_BLOCK_SIZE);
                Debug.LogError("Crypt: 4");
                return null;
            }
            _decryptHistory[_cryptSetup.ServerNonce[0]] = _cryptSetup.ServerNonce[1];

            if (restore)
            {
                //Debug.Log("Restoring");
                Array.Copy(saveiv, 0, _cryptSetup.ServerNonce, 0, AES_BLOCK_SIZE);
            }

            _good++;
            _late += late;
            _lost += lost;

            return dst;
        }

        private void OcbDecrypt(
            byte[] encrypted,
            int len,
            byte[] plain,
            byte[] nonce,
            byte[] tag,
            int encrypted_offset)
        {

            ZERO(checksum);
            _encryptor.TransformBlock(nonce, 0, AES_BLOCK_SIZE, delta, 0);

            int offset = 0;
            while (len > AES_BLOCK_SIZE)
            {
                S2(delta);
                Xor(tmp, delta, encrypted, 0, 0, offset + encrypted_offset);
                _decryptor.TransformBlock(tmp, 0, AES_BLOCK_SIZE, tmp, 0);

                Xor(plain, delta, tmp, offset, 0, 0);
                Xor(checksum, checksum, plain, 0, 0, offset);

                len -= AES_BLOCK_SIZE;
                offset += AES_BLOCK_SIZE;
            }

            S2(delta);
            ZERO(tmp);

            long num = len*8;
            tmp[AES_BLOCK_SIZE - 2] = (byte)((num >> 8) & 0xFF);
            tmp[AES_BLOCK_SIZE - 1] = (byte) (num & 0xFF);
            Xor(tmp, tmp, delta);

            _encryptor.TransformBlock(tmp, 0, AES_BLOCK_SIZE, pad, 0);

            ZERO(tmp);
            Array.Copy(encrypted, offset + encrypted_offset, tmp, 0, len);

            Xor(tmp, tmp, pad);
            Xor(checksum, checksum, tmp);

            Array.Copy(tmp, 0, plain, offset, len);

            S3(delta);
            Xor(tmp, delta, checksum);
            _encryptor.TransformBlock(tmp, 0, AES_BLOCK_SIZE, tag, 0);
        }
    }
}