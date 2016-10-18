using UnityEngine;
using System.Collections;

namespace Mumble
{
    public class SendMumbleAudio : MonoBehaviour
    {
        private MumbleClient _mumbleClient;
        private AudioClip _sendAudioClip;
        private bool isRecording = false;
        private string _currentMic;
        private int _previousPosition = 0;

        public void Initialize(MumbleClient mumbleClient)
        {
            _mumbleClient = mumbleClient;
            GetCurrentMic();
        }
        void GetCurrentMic()
        {
            foreach (string device in Microphone.devices)
            {
                _currentMic = device;
                int minFreq;
                int maxFreq;
                Microphone.GetDeviceCaps(_currentMic, out minFreq, out maxFreq);
                print("Device:  " + _currentMic + " has freq: " + minFreq + " to " + maxFreq);
            }
            _currentMic = Microphone.devices[1];
        }
        void SendVoiceIfReady()
        {
            int currentPosition = Microphone.GetPosition(_currentMic);
            while(currentPosition - _previousPosition >= _mumbleClient.NumSamplesPerFrame)
            {
                //print("Sending sample of size: " + _mumbleClient.NumSamplesPerFrame);
                float[] tempSampleStore = new float[_mumbleClient.NumSamplesPerFrame];
                _sendAudioClip.GetData(tempSampleStore, _previousPosition);
                _mumbleClient.SendVoicePacket(tempSampleStore);
                _previousPosition += _mumbleClient.NumSamplesPerFrame;
            }
        }
        void Update()
        {
            if (_mumbleClient == null || !_mumbleClient.ConnectionSetupFinished)
                return;

            if (Input.GetKeyDown(KeyCode.Space))
            {
                _sendAudioClip = Microphone.Start(_currentMic, true, 1, 44100);
                _previousPosition = 0;
                isRecording = true;
            }
            if (Input.GetKeyUp(KeyCode.Space))
            {
                Microphone.End(_currentMic);
                isRecording = false;
            }
            if (isRecording)
                SendVoiceIfReady();
        }
    }
}
