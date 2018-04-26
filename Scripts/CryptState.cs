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

        // Used by Encrypt
        private readonly byte[] _enc_tag = new byte[AES_BLOCK_SIZE];

        // Used by OCB Encrypt
        private readonly byte[] _enc_checksum = new byte[AES_BLOCK_SIZE];
        private readonly byte[] _enc_tmp = new byte[AES_BLOCK_SIZE];
        private readonly byte[] _enc_delta = new byte[AES_BLOCK_SIZE];
        private readonly byte[] _enc_pad = new byte[AES_BLOCK_SIZE];

        // Used by Decrypt
        private readonly byte[] _dec_saveiv = new byte[AES_BLOCK_SIZE];
        private readonly byte[] _dec_tag = new byte[AES_BLOCK_SIZE];
        // Used by OCB Decrypt
        private readonly byte[] _dec_checksum = new byte[AES_BLOCK_SIZE];
        private readonly byte[] _dec_tmp = new byte[AES_BLOCK_SIZE];
        private readonly byte[] _dec_delta = new byte[AES_BLOCK_SIZE];
        private readonly byte[] _dec_pad = new byte[AES_BLOCK_SIZE];

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

            var dst = new byte[length + 4];
            OcbEncrypt(inBytes, length, dst, _cryptSetup.ClientNonce, _enc_tag, 4);
            dst[0] = _cryptSetup.ClientNonce[0];
            dst[1] = _enc_tag[0];
            dst[2] = _enc_tag[1];
            dst[3] = _enc_tag[2];

            return dst;
        }

        private void OcbEncrypt(byte[] plain, int plainLength, byte[] encrypted, byte[] nonce, byte[] tag, int encrypted_offset)
        {
            ZERO(_enc_checksum);
            _encryptor.TransformBlock(nonce, 0, AES_BLOCK_SIZE, _enc_delta, 0);

            int offset = 0;
            int len = plainLength;
            while (len > AES_BLOCK_SIZE)
            {
                S2(_enc_delta);
                Xor(_enc_checksum, _enc_checksum, plain, 0, 0, offset);
                Xor(_enc_tmp, _enc_delta, plain, 0, 0, offset);

                _encryptor.TransformBlock(_enc_tmp, 0, AES_BLOCK_SIZE, _enc_tmp, 0);

                Xor(encrypted, _enc_delta, _enc_tmp, offset + encrypted_offset, 0, 0);
                offset += AES_BLOCK_SIZE;
                len -= AES_BLOCK_SIZE;
            }

            S2(_enc_delta);
            ZERO(_enc_tmp);
            long num = len*8;
            _enc_tmp[AES_BLOCK_SIZE - 2] = (byte) ((num >> 8) & 0xFF);
            _enc_tmp[AES_BLOCK_SIZE - 1] = (byte) (num & 0xFF);
            Xor(_enc_tmp, _enc_tmp, _enc_delta);

            _encryptor.TransformBlock(_enc_tmp, 0, AES_BLOCK_SIZE, _enc_pad, 0);

            Array.Copy(plain, offset, _enc_tmp, 0, len);
            Array.Copy(_enc_pad, len, _enc_tmp, len, AES_BLOCK_SIZE - len);

            Xor(_enc_checksum, _enc_checksum, _enc_tmp);
            Xor(_enc_tmp, _enc_pad, _enc_tmp);
            Array.Copy(_enc_tmp, 0, encrypted, offset + encrypted_offset, len);

            S3(_enc_delta);
            Xor(_enc_tmp, _enc_delta, _enc_checksum);

            _encryptor.TransformBlock(_enc_tmp, 0, AES_BLOCK_SIZE, tag, 0);
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

            Array.Copy(_cryptSetup.ServerNonce, 0, _dec_saveiv, 0, AES_BLOCK_SIZE);

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
                    lost = 256 - _cryptSetup.ServerNonce[0] + ivbyte - 1;
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
                    Array.Copy(_dec_saveiv, 0, _cryptSetup.ServerNonce, 0, AES_BLOCK_SIZE);
                    Debug.LogError("Crypt: 3");
                    return null;
                }
            }

            int plainLength = length - 4;
            var dst = new byte[plainLength];
            OcbDecrypt(source, plainLength, dst, _cryptSetup.ServerNonce, _dec_tag, 4);

            if (_dec_tag[0] != source[1]
                || _dec_tag[1] != source[2]
                || _dec_tag[2] != source[3])
            {

                Array.Copy(_dec_saveiv, 0, _cryptSetup.ServerNonce, 0, AES_BLOCK_SIZE);
                Debug.LogError("Crypt: 4");
                //Debug.LogError("Crypt: 4 good:" + _good + " lost: " + _lost + " late: " + _late);
                return null;
            }
            _decryptHistory[_cryptSetup.ServerNonce[0]] = _cryptSetup.ServerNonce[1];

            if (restore)
            {
                //Debug.Log("Restoring");
                Array.Copy(_dec_saveiv, 0, _cryptSetup.ServerNonce, 0, AES_BLOCK_SIZE);
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

            ZERO(_dec_checksum);
            _encryptor.TransformBlock(nonce, 0, AES_BLOCK_SIZE, _dec_delta, 0);

            int offset = 0;
            while (len > AES_BLOCK_SIZE)
            {
                S2(_dec_delta);
                Xor(_dec_tmp, _dec_delta, encrypted, 0, 0, offset + encrypted_offset);
                _decryptor.TransformBlock(_dec_tmp, 0, AES_BLOCK_SIZE, _dec_tmp, 0);

                Xor(plain, _dec_delta, _dec_tmp, offset, 0, 0);
                Xor(_dec_checksum, _dec_checksum, plain, 0, 0, offset);

                len -= AES_BLOCK_SIZE;
                offset += AES_BLOCK_SIZE;
            }

            S2(_dec_delta);
            ZERO(_dec_tmp);

            long num = len * 8;
            _dec_tmp[AES_BLOCK_SIZE - 2] = (byte)((num >> 8) & 0xFF);
            _dec_tmp[AES_BLOCK_SIZE - 1] = (byte) (num & 0xFF);
            Xor(_dec_tmp, _dec_tmp, _dec_delta);

            _encryptor.TransformBlock(_dec_tmp, 0, AES_BLOCK_SIZE, _dec_pad, 0);

            ZERO(_dec_tmp);
            Array.Copy(encrypted, offset + encrypted_offset, _dec_tmp, 0, len);

            Xor(_dec_tmp, _dec_tmp, _dec_pad);
            Xor(_dec_checksum, _dec_checksum, _dec_tmp);

            Array.Copy(_dec_tmp, 0, plain, offset, len);

            S3(_dec_delta);
            Xor(_dec_tmp, _dec_delta, _dec_checksum);
            _encryptor.TransformBlock(_dec_tmp, 0, AES_BLOCK_SIZE, tag, 0);
        }
    }
}