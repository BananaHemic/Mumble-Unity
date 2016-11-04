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
        }
        void OnAudioPositionSet(int position)
        {
            //print("Position set " + position);
            _position = position;
        }
    }
}
