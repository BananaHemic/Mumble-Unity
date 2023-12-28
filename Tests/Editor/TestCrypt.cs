using MumbleProto;
using NUnit.Framework;
using UnityEngine;

namespace Mumble.Editor.Tests
{
    [TestFixture]
    public class TestCrypt
    {
        const int buffer_length = 256;
        const int num_trials = 1024;

        [Test]
        public void TestCanEncryptAndDecrypt()
        {
            CryptState encoderState = new();
            CryptState decoderState = new();
            CryptSetup encoderSetup = new();
            CryptSetup decoderSetup = new();
            // Make the key and nonces random
            encoderSetup.Key = GetRandomArray(16);
            encoderSetup.ClientNonce = GetRandomArray(16);
            encoderSetup.ServerNonce = GetRandomArray(16);

            // The decoder uses the same stuff, but with client/server nonce exchanged with one another
            decoderSetup.Key = encoderSetup.Key;
            decoderSetup.ClientNonce = (byte[])encoderSetup.ServerNonce.Clone();
            decoderSetup.ServerNonce = (byte[])encoderSetup.ClientNonce.Clone();

            encoderState.CryptSetup = encoderSetup;
            decoderState.CryptSetup = decoderSetup;

            byte[] buffer = GetRandomArray(buffer_length);

            for (int trial = 0; trial < num_trials; trial++)
            {
                Randomize(buffer);
                byte[] encrypted = encoderState.Encrypt(buffer, buffer_length);
                byte[] decrypted = decoderState.Decrypt(encrypted, encrypted.Length);

                if (encrypted == null)
                    Debug.Log("Nothing decrypted?");
                if (decrypted == null)
                    Debug.Log("Nothing decrypted?");

                for (int i = 0; i < buffer_length; i++)
                    Assert.AreEqual(buffer[i], decrypted[i]);
            }
            Debug.Log("Done");
        }

        byte[] GetRandomArray(int len)
        {
            byte[] ray = new byte[len];

            Randomize(ray);
            return ray;
        }

        void Randomize(byte[] ray)
        {
            for (int i = 0; i < ray.Length; i++)
                ray[i] = (byte)Random.Range(0, 255);
        }
    }
}