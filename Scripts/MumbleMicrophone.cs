using UnityEngine;
using System.Collections;

namespace Mumble
{
    public class MumbleMicrophone : MonoBehaviour
    {
        public int MicNumberToUse;
        public AudioClip TestingClipToUse;
        public bool AlwaysSendAudio = true;
        public KeyCode PushToTalkKeycode = KeyCode.Space;

        const int NumRecordingSeconds = 24;
        private int NumSamplesInAudioClip {
            get
            {
                return NumRecordingSeconds * _mumbleClient.EncoderSampleRate;
            }
        }
        public int NumSamplesPerOutgoingPacket { get; private set; }

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
        }
        /// <summary>
        /// Find the microphone to use and return it's sample rate
        /// </summary>
        /// <returns>New Mic's sample rate</returns>
        internal int GetCurrentMicSampleRate()
        {
            //Make sure the requested mic index exists
            if (Microphone.devices.Length <= MicNumberToUse)
                return -1;

            int minFreq;
            int maxFreq;
            Microphone.GetDeviceCaps(_currentMic, out minFreq, out maxFreq);

            int micSampleRate = MumbleClient.GetNearestSupportedSampleRate(maxFreq);
            NumSamplesPerOutgoingPacket = MumbleConstants.NUM_FRAMES_PER_OUTGOING_PACKET * micSampleRate / 100;

            print("Device:  " + _currentMic + " has freq: " + minFreq + " to " + maxFreq + " setting to: " + micSampleRate);
            _currentMic = Microphone.devices[MicNumberToUse];

            if (AlwaysSendAudio)
                StartSendingAudio(micSampleRate);
            return micSampleRate;
        }
        void SendVoiceIfReady()
        {
            int currentPosition = Microphone.GetPosition(_currentMic);

            if(currentPosition < _previousPosition)
                _numTimesLooped++;

            int totalSamples = _numTimesLooped * NumSamplesInAudioClip + currentPosition;
            _previousPosition = currentPosition;

            while(totalSamples - _totalNumSamplesSent >= NumSamplesPerOutgoingPacket)
            {
                PcmArray newData = _mumbleClient.GetAvailablePcmArray();

                if (!_mumbleClient.UseSyntheticSource)
                    _sendAudioClip.GetData(newData.Pcm, _totalNumSamplesSent % NumSamplesInAudioClip);
                else {
                    TestingClipToUse.GetData(newData.Pcm, _totalNumSamplesSent % NumSamplesInAudioClip);
                }

                _mumbleClient.SendVoicePacket(newData);
                _totalNumSamplesSent += NumSamplesPerOutgoingPacket;
            }
        }
        public void StartSendingAudio(int sampleRate)
        {
            Debug.LogWarning("Starting to send audio");
            _sendAudioClip = Microphone.Start(_currentMic, true, NumRecordingSeconds, sampleRate);
            _previousPosition = 0;
            _numTimesLooped = 0;
            _totalNumSamplesSent = 0;
            isRecording = true;
        }
        public void StopSendingAudio()
        {
            Microphone.End(_currentMic);
            _mumbleClient.StopSendingVoice();
            isRecording = false;
        }
        void Update()
        {
            if (_mumbleClient == null || !_mumbleClient.ConnectionSetupFinished)
                return;

            if (!AlwaysSendAudio && Input.GetKeyDown(PushToTalkKeycode))
                StartSendingAudio(_mumbleClient.EncoderSampleRate);
            if (!AlwaysSendAudio && Input.GetKeyUp(PushToTalkKeycode))
                StopSendingAudio();
            if (isRecording)
                SendVoiceIfReady();
        }
    }
}
