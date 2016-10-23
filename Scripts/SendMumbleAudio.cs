using UnityEngine;
using System.Collections;

namespace Mumble
{
    public class SendMumbleAudio : MonoBehaviour
    {
        public int MicNumberToUse;
        public AudioClip TestingClipToUse;

        const int NumRecordingSeconds = 24;
        const int NumSamples = NumRecordingSeconds * Constants.SAMPLE_RATE;

        private MumbleClient _mumbleClient;
        private AudioClip _sendAudioClip;
        private bool isRecording = false;
        private string _currentMic;
        private int _previousPosition = 0;
        private int _totalNumSamplesSent = 0;
        private int _numTimesLooped = 0;
        
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
            _currentMic = Microphone.devices[MicNumberToUse];
        }
        void SendVoiceIfReady()
        {
            int currentPosition = Microphone.GetPosition(_currentMic);

            if(currentPosition < _previousPosition)
                _numTimesLooped++;

            int totalSamples = _numTimesLooped * NumSamples + currentPosition;
            _previousPosition = currentPosition;

            while(totalSamples - _totalNumSamplesSent >= _mumbleClient.NumSamplesPerFrame)
            {

                //print("Sending sample of size: " + _mumbleClient.NumSamplesPerFrame);
                //TODO use a big buffer that we load parts into
                float[] tempSampleStore = new float[_mumbleClient.NumSamplesPerFrame];

                if (!MumbleClient.UseSyntheticMic)
                    _sendAudioClip.GetData(tempSampleStore, _totalNumSamplesSent % NumSamples);
                else {
                    TestingClipToUse.GetData(tempSampleStore, _totalNumSamplesSent % NumSamples);
                    /*
                    for (int i = 0; i < tempSampleStore.Length; i++)
                    {
                        tempSampleStore[i] = Mathf.Sin(i * 100f);
                    }
                    */
                }

                _mumbleClient.SendVoicePacket(tempSampleStore);
                _totalNumSamplesSent += _mumbleClient.NumSamplesPerFrame;
            }
        }
        void Update()
        {
            if (_mumbleClient == null || !_mumbleClient.ConnectionSetupFinished)
                return;

            if (Input.GetKeyDown(KeyCode.Space))
            {
                _sendAudioClip = Microphone.Start(_currentMic, true, NumRecordingSeconds, Constants.SAMPLE_RATE);
                _previousPosition = 0;
                _numTimesLooped = 0;
                _totalNumSamplesSent = 0;
                isRecording = true;
            }
            if (Input.GetKeyUp(KeyCode.Space))
            {
                Microphone.End(_currentMic);
                _mumbleClient.StopSendingVoice();
                isRecording = false;
            }
            if (isRecording)
                SendVoiceIfReady();
        }
    }
}
