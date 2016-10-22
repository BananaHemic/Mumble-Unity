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

        const int ClipSampleLength = 9600;
        const int SampleLengthSeconds = 1;
        const bool isStreamingAudio = true;

        void Start() {
            Source = GetComponent<AudioSource>();
            _clip = AudioClip.Create("Received Audio", Constants.SAMPLE_RATE * SampleLengthSeconds, Constants.NUM_CHANNELS, Constants.SAMPLE_RATE, isStreamingAudio, OnAudioRead, OnAudioPositionSet);
            Source.clip = _clip;
            Source.Play();

        }
        public void SetMumbleClient(MumbleClient mumbleClient)
        {
            _mumbleClient = mumbleClient;
            _mumbleUser = _mumbleClient.GetUserAtTarget(17);
        }
        void OnAudioRead(float[] data)
        {
            if (_mumbleClient == null || _mumbleUser == null || !_mumbleClient.ConnectionSetupFinished)
                return;

            _mumbleUser.Voice.Read(data, 0, data.Length);
            
            /*
            for(int i = 0; i < data.Length; i++)
            {
                data[i] *= 127f;// *= -32767f;
                if (data[i] > 1f)
                    data[i] = 1f;
                if (data[i] < -1f)
                    data[i] = -1f;
            }
            */
            
            float sum = 0;
            float sqrdSum = 0;
            float max = float.MinValue;
            int indexOfMax = -1;
            int numZeros = 0;

            for(int i = 0; i < data.Length; i++)
            {
                sum+=Mathf.Abs(data[i]);
                sqrdSum += data[i] * data[i];
                if (data[i] > max)
                {
                    max = data[i];
                    indexOfMax = i;
                }
                if (data[i] == 0)
                    numZeros++;
            }
            sum = sum / data.Length;
            sqrdSum = Mathf.Sqrt(sqrdSum) / data.Length;

            /*
            Debug.Log("Read has average of: " + sum + " max of: " + max + " at: " + indexOfMax
                + " sqrdAvg = " + sqrdSum + " number of zeros = " + numZeros);
            /*
            print("reading: "
                + " " + data[0]
                + " " + data[1]
                + " " + data[2]
                + " " + data[3]
                + " " + data[4]
                + " " + data[5]
                + " " + data[6]
                );
                */
        }
        void OnAudioPositionSet(int position)
        {
            _position = position;
        }
    }
}
