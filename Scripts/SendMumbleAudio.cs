using UnityEngine;
using System.Collections;

namespace Mumble
{
    public class SendMumbleAudio : MonoBehaviour
    {
        public int MicNumberToUse;
        public AudioClip TestingClipToUse;
        public bool AlwaysSendAudio;
        public KeyCode PushToTalkKeycode;

        const int NumRecordingSeconds = 24;
        private int MicSampleRate;
        private int NumSamplesInAudioClip {
            get
            {
                return NumRecordingSeconds * MicSampleRate;
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
            GetCurrentMic();
        }
        void GetCurrentMic()
        {
            int minFreq;
            int maxFreq;
            Microphone.GetDeviceCaps(_currentMic, out minFreq, out maxFreq);

            MicSampleRate = MumbleClient.GetNearestSupportedSampleRate(maxFreq);
            NumSamplesPerOutgoingPacket = MumbleConstants.NUM_FRAMES_PER_OUTGOING_PACKET * MicSampleRate / 100;

            print("Device:  " + _currentMic + " has freq: " + minFreq + " to " + maxFreq + " setting to: " + MicSampleRate);
            _currentMic = Microphone.devices[MicNumberToUse];
            _mumbleClient.SetEncodingFrequency(MicSampleRate);

            if (AlwaysSendAudio)
                StartSendingAudio();
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
                //print("Sending sample of size: " + _mumbleClient.NumSamplesPerFrame);
                //TODO use a big buffer that we load parts into
                PcmArray newData = _mumbleClient.GetAvailablePcmArray();

                print("NumSamplesInAudioClip: " + NumSamplesInAudioClip + " _totalNumSamplesSent: " + _totalNumSamplesSent);
                print("% " + (_totalNumSamplesSent % NumSamplesInAudioClip));
                print(" len: " + newData.Pcm.Length);

                if (!_mumbleClient.UseSyntheticSource)
                    _sendAudioClip.GetData(newData.Pcm, _totalNumSamplesSent % NumSamplesInAudioClip);
                else {
                    TestingClipToUse.GetData(newData.Pcm, _totalNumSamplesSent % NumSamplesInAudioClip);
                    /*
                    for (int i = 0; i < tempSampleStore.Length; i++)
                    {
                        tempSampleStore[i] = Mathf.Sin(i * 100f);
                    }
                    */
                }

                _mumbleClient.SendVoicePacket(newData);
                _totalNumSamplesSent += NumSamplesPerOutgoingPacket;
                print("Encoded " + NumSamplesPerOutgoingPacket + " samples");
            }
        }
        void StartSendingAudio()
        {
            _sendAudioClip = Microphone.Start(_currentMic, true, NumRecordingSeconds, MicSampleRate);
            _previousPosition = 0;
            _numTimesLooped = 0;
            _totalNumSamplesSent = 0;
            isRecording = true;
        }
        void StopSendingAudio()
        {
            Microphone.End(_currentMic);
            _mumbleClient.StopSendingVoice();
            isRecording = false;
        }
        void Update()
        {
            if (_mumbleClient == null || !_mumbleClient.ConnectionSetupFinished)
                return;

            if (Input.GetKeyDown(PushToTalkKeycode))
                StartSendingAudio();
            if (Input.GetKeyUp(PushToTalkKeycode))
                StopSendingAudio();
            if (isRecording)
                SendVoiceIfReady();
        }
    }
}
