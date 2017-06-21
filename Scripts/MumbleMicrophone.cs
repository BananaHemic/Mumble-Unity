using UnityEngine;
using System.Collections;

namespace Mumble
{
    public class MumbleMicrophone : MonoBehaviour
    {
        public enum MicType
        {
            AlwaysSend,
            //SignalToNoise, //TODO we need a dll to calculate the signal to noise, as it requires a FFT which I don't want to do in C#
            Amplitude,
            PushToTalk,
            MethodBased // Start / Stop speaking based on calls to this method
        }
        public bool SendAudioOnStart = true;
        public int MicNumberToUse;
        /// <summary>
        /// The minimum aplitude to recognize as voice data
        /// Only used if Mic is set to "Amplitude"
        /// </summary>
        [Range (0.0f, 1.0f)]
        public float MinAmplitude = 0.007f;
        public float VoiceHoldSeconds = 0.5f;
        public MicType VoiceSendingType = MicType.AlwaysSend;
        public AudioClip TestingClipToUse;
        public KeyCode PushToTalkKeycode = KeyCode.Space;

        const int NumRecordingSeconds = 1;
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
        // Amplitude MicType vars
        private int _voiceHoldSamples;
        private int _sampleNumberOfLastMinAmplitudeVoice;
        
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

            _voiceHoldSamples = Mathf.RoundToInt(micSampleRate * VoiceHoldSeconds);

            if (SendAudioOnStart && (VoiceSendingType == MicType.AlwaysSend
                || VoiceSendingType == MicType.Amplitude))
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
                _totalNumSamplesSent += NumSamplesPerOutgoingPacket;

                if(VoiceSendingType == MicType.Amplitude)
                {
                    if (AmplitudeHigherThan(MinAmplitude, newData.Pcm))
                    {
                        _sampleNumberOfLastMinAmplitudeVoice = _totalNumSamplesSent;
                    }
                    else
                    {
                        if (_totalNumSamplesSent > _sampleNumberOfLastMinAmplitudeVoice + _voiceHoldSamples)
                            return;
                    }
                }
                _mumbleClient.SendVoicePacket(newData);
            }
        }
        private bool AmplitudeHigherThan(float minAmplitude, float[] pcm)
        {
            float currentSum = pcm[0];
            int checkInterval = 200;

            for(int i = 1; i < pcm.Length; i++)
            {
                currentSum += Mathf.Abs(pcm[i]);
                // Allow early returning
                if (i % checkInterval == 0 && currentSum / i > minAmplitude)
                    return true;
            }
            return currentSum / pcm.Length > minAmplitude;
        }
        public void StartSendingAudio(int sampleRate)
        {
            Debug.Log("Starting to send audio");
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

            if (VoiceSendingType == MicType.PushToTalk)
            {
                if (Input.GetKeyDown(PushToTalkKeycode))
                    StartSendingAudio(_mumbleClient.EncoderSampleRate);
                if (Input.GetKeyUp(PushToTalkKeycode))
                    StopSendingAudio();
            }
            if (isRecording)
                SendVoiceIfReady();
        }
    }
}
