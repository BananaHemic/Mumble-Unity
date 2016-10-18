using UnityEngine;
using System.Collections;
using System;

namespace Mumble {
    [RequireComponent(typeof(AudioSource))]
    public class MumbleAudioPlayer : MonoBehaviour {

        private AudioSource Source;
        private AudioClip _clip;
        private MumbleClient _mumbleClient;
        private User _mumbleUser;
        private int _position = 0;

        const int Channels = 1;
        const int Frequency = 48000;
        const int ClipSampleLength = 9600;
        const bool isStreamingAudio = true;

        void Start() {
            Source = GetComponent<AudioSource>();
            _clip = AudioClip.Create("Received Audio", Frequency * 2, Channels, Frequency, isStreamingAudio, OnAudioRead, OnAudioPositionSet);
            //_clip = AudioClip.Create("Received Audio", ClipSampleLength, Channels, Frequency, isStreamingAudio);
            Source.clip = _clip;
            Source.Play();

        }
        public void SetMumbleClient(MumbleClient mumbleClient)
        {
            _mumbleClient = mumbleClient;
            _mumbleUser = _mumbleClient.GetUserAtTarget(17);
        }
        //NB this expects the data array to be loaded, not just set!
        void OnAudioRead(float[] data)
        {
            if (_mumbleClient == null || _mumbleUser == null || !_mumbleClient.ConnectionSetupFinished)
                return;

            int originalLength = data.Length;

            float[] newData = GetNextFloats(data.Length);
            
            for(int i = 0; i < data.Length; i++)
            {
                newData[i] = newData[i];// Mathf.Sin(2 * Mathf.PI * 4400 * _position / Frequency);//1f;// -1f * data[i];
                data[i] = newData[i];// Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 4400 * _position / Frequency));//1f;// -1f * data[i];
                _position++;
            }

            /*
            Debug.LogWarning("Making noise with: "
                + " " + data[0]
                + " " + data[1]
                + " " + data[2]
                + " " + data[3]
                + " " + data[4]
                + " " + data[5]
                + " " + data[6]
                + " " + data[7]
                );
                */

            if (data.Length != originalLength)
                Debug.LogError("Wrong data size! We have " + data.Length + " we need " + originalLength);
            /*
            //_clip.SetData(samples, 0);
            _position = 0;
            print("reading");
            int count = 0;
            while (count < data.Length)
            {
                data[count] = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 4400 * _position / Frequency));

                _position++;
                count++;
            }
            */
        }
        private float[] GetNextFloats(int numFloatsToReturn)
        {
            //Convert from PCM16 to float array
            int numBytesToRead = numFloatsToReturn * sizeof(Int16);
            byte[] buff = new byte[numBytesToRead];
            _mumbleUser.Voice.Read(buff, 0, buff.Length);
            /*
            print("Buffer length = " + buff.Length);
            print("buffer starts with "
                + " " + buff[0]
                + " " + buff[1]
                + " " + buff[2]
                + " " + buff[3]
                + " " + buff[4]
                + " " + buff[5]
                + " " + buff[6]
                + " " + buff[7]
                );
                */
            Int16[] intSamples = Bytes2Ints(buff);
            
            /*
            print("int samples start with: "
                + " " + intSamples[0]
                + " " + intSamples[1]
                + " " + intSamples[2]
                + " " + intSamples[3]
                + " " + intSamples[4]
                + " " + intSamples[5]
                + " " + intSamples[6]
                + " " + intSamples[7]
                );
                */
                
            float[] samples = new float[intSamples.Length];

            //Postprocess samples to convert PCM16 to [-1,1]
            for(int i = 0; i < intSamples.Length; i++)
            {
                float newFloat = ((float)intSamples[i]) / (float)32768;
                if (newFloat > 1f || newFloat < -1f)
                    Debug.LogWarning("Out of range float: " + newFloat);
                samples[i] = newFloat;
            }
            return samples;

            /*
            int numBytesToRead = numFloatsToReturn * sizeof(float);

            byte[] buff = new byte[numBytesToRead];
            _mumbleUser.Voice.Read(buff, 0, ClipSampleLength);
            print("Buffer length = " + buff.Length);
            print("buffer starts with "
                + " " + buff[0]
                + " " + buff[1]
                + " " + buff[2]
                + " " + buff[3]
                + " " + buff[4]
                + " " + buff[5]
                + " " + buff[6]
                + " " + buff[7]
                );
            float[] samples = Bytes2Floats(buff);
            print("samples start with: " + samples[0]
                + " " + samples[1]
                + " " + samples[2]
                + " " + samples[3]
                + " " + samples[4]
                + " " + samples[5]
                + " " + samples[6]
                + " " + samples[7]
                );
            print("Sample length = " + samples.Length);
            return samples;
            */
        }
        void OnAudioPositionSet(int position)
        {
            //Debug.LogWarning("position set: " + position);
            _position = position;
        }
        Int16[] Bytes2Ints(byte[] array)
        {
            Int16[] intArr = new Int16[array.Length / sizeof(Int16)];

            for (int i = 0; i < intArr.Length; i++)
            {
                if (Constants.IS_LITTLE_ENDIAN)
                    Array.Reverse(array, i * sizeof(Int16), sizeof(Int16));
                intArr[i] = BitConverter.ToInt16(array, i * sizeof(Int16));
            }
            return intArr;
        }
        float[] Bytes2Floats(byte[] array)
        {
            float[] floatArr = new float[array.Length / 4];
            for (int i = 0; i < floatArr.Length; i++)
            {
                if (Constants.IS_LITTLE_ENDIAN)
                    Array.Reverse(array, i * 4, 4);
                floatArr[i] = BitConverter.ToSingle(array, i * 4);
            }
            return floatArr;
        }
        /*
        void Update () {

            if (_mumbleClient == null)
                return;

            if (Input.GetKeyDown(KeyCode.A))
            {
                print("Loading data");
                byte[] buff = new byte[ClipSampleLength];
                _mumbleUser.Voice.Read( buff, 0, ClipSampleLength);
                print("Buffer length = " + buff.Length);
                print("buffer starts with "
                    + " " + buff[0]
                    + " " + buff[1]
                    + " " + buff[2]
                    + " " + buff[3]
                    + " " + buff[4]
                    + " " + buff[5]
                    + " " + buff[6]
                    + " " + buff[7]
                    );
                float[] samples = Bytes2Floats(buff);
                print("samples start with: " + samples[0]
                    + " " + samples[1]
                    + " " + samples[2]
                    + " " + samples[3]
                    + " " + samples[4]
                    + " " + samples[5]
                    + " " + samples[6]
                    + " " + samples[7]
                    );
                print("Sample length = " + samples.Length);
                _clip.SetData(samples, 0);
            }
            if (Input.GetKeyDown(KeyCode.L))
            {
                print("Will play audio");
                Source.Play();
            }
        }
        */
    }
}
