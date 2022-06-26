using UnityEngine;
using System.Collections;
using UnityEngine.UI;

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
        /// <summary>
        /// Delegate called when the user sends out a sample of their audio
        /// Use when you want to plug in your own audio pre-processor
        /// </summary>
        /// <param name="array"></param>
        public delegate void OnMicrophoneData(PcmArray array);
        public OnMicrophoneData OnMicData;

        /// <summary>
        /// Allows your app to send positional data to other mumble users
        /// The length of the positional data is configured in MumbleClient
        /// </summary>
        /// <param name="positionalData"></param>
        public delegate void WritePositionalData(ref byte[] positionalData, ref int posDataLength);

        public delegate void OnMicDisconnected();
        public event OnMicDisconnected OnMicDisconnect;

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

        /// <summary>
        /// How long to make the audio buffer that Unity
        /// creates to store the mic data. Smaller numbers
        /// are more memory efficient, but there seems to
        /// be a Unity bug where there's a pop at the end
        /// of the audio clip. Larger values seem to better
        /// hide this pop
        /// </summary>
        //const int NumRecordingSeconds = 1;
        const int NumRecordingSeconds = 5;
        private int NumSamplesInMicBuffer {
            get
            {
                return NumRecordingSeconds * _mumbleClient.EncoderSampleRate;
            }
        }
        public int NumSamplesPerOutgoingPacket { get; private set; }
        public AudioClip SendAudioClip { get; private set; }

        private MumbleClient _mumbleClient;
        private bool isRecording = false;
        private string _currentMic;
        private int _previousPosition = 0;
        private int _totalNumSamplesSent = 0;
        private int _numTimesLooped = 0;
        // Amplitude MicType vars
        private int _voiceHoldSamples;
        private int _sampleNumberOfLastMinAmplitudeVoice;
        private float _secondsWithoutMicSamples = 0;
        private WritePositionalData _writePositionalDataFunc = null;
        // How many seconds to wait before we consider the mic being disconnected
        const float MaxSecondsWithoutMicData = 1f;
        
        public void Initialize(MumbleClient mumbleClient)
        {
            _mumbleClient = mumbleClient;
        }

        public void SetPositionalDataFunction(WritePositionalData writePositionalData)
        {
            _writePositionalDataFunc = writePositionalData;
        }
        public void setSettings(MicType microphoneSetting, int device){
            VoiceSendingType = microphoneSetting;
            MicNumberToUse = device;
        }
        /// <summary>
        /// Find the microphone to use and return it's sample rate
        /// </summary>
        /// <returns>New Mic's sample rate</returns>
        internal int InitializeMic()
        {
            //Make sure the requested mic index exists
            if (Microphone.devices.Length <= MicNumberToUse)
            {
                Debug.LogWarning("No microphone connected!");
                return -1;
            }

            _currentMic = Microphone.devices[MicNumberToUse];
            int minFreq;
            int maxFreq;
            Microphone.GetDeviceCaps(_currentMic, out minFreq, out maxFreq);

            int micSampleRate = MumbleClient.GetNearestSupportedSampleRate(maxFreq);
            NumSamplesPerOutgoingPacket = MumbleConstants.NUM_FRAMES_PER_OUTGOING_PACKET * micSampleRate / 100;

            if (micSampleRate != 48000)
                Debug.LogWarning("Using a possibly unsupported sample rate of " + micSampleRate + " things might get weird");
            Debug.Log("Device:  " + _currentMic + " has freq: " + minFreq + " to " + maxFreq + " setting to: " + micSampleRate);

            _voiceHoldSamples = Mathf.RoundToInt(micSampleRate * VoiceHoldSeconds);

            if (SendAudioOnStart && (VoiceSendingType == MicType.AlwaysSend
                || VoiceSendingType == MicType.Amplitude))
                StartSendingAudio(micSampleRate);

            return micSampleRate;
        }
        public int GetMicPosition()
        {
            if (_currentMic == null)
                return 0;
            return Microphone.GetPosition(_currentMic);
        }
        public void SetBitrate(int bitrate)
        {
            _mumbleClient.SetBitrate(bitrate);
        }
        public int GetBitrate()
        {
            return _mumbleClient.GetBitrate();
        }
        void SendVoiceIfReady()
        {
            int currentPosition = Microphone.GetPosition(_currentMic);
            //Debug.Log(currentPosition + " " + Microphone.IsRecording(_currentMic));

            //Debug.Log(currentPosition);
            if (currentPosition < _previousPosition)
                _numTimesLooped++;

            //Debug.Log("mic position: " + currentPosition + " was: " + _previousPosition + " looped: " + _numTimesLooped);

            int totalSamples = currentPosition + _numTimesLooped * NumSamplesInMicBuffer;
            bool isEmpty = currentPosition == 0 && _previousPosition == 0;
            bool isFirstSample = _numTimesLooped == 0 && _previousPosition == 0;
            _previousPosition = currentPosition;
            if (isEmpty)
                _secondsWithoutMicSamples += Time.deltaTime;
            else
                _secondsWithoutMicSamples = 0;

            if(_secondsWithoutMicSamples > MaxSecondsWithoutMicData)
            {
                // For 5 times in a row, we received no usable data
                // this normally means that the mic we were using disconnected
                Debug.Log("Mic has disconnected!");
                StopSendingAudio();
                if (OnMicDisconnect != null)
                    OnMicDisconnect();
                return;
            }

            // We drop the first sample, because it generally starts with
            // a lot of pre-existing, stale, audio data which we couldn't
            // use b/c it's too old
            if (isFirstSample)
            {
                _totalNumSamplesSent = totalSamples;
                return;
            }

            while(totalSamples - _totalNumSamplesSent >= NumSamplesPerOutgoingPacket)
            {
                PcmArray newData = _mumbleClient.GetAvailablePcmArray();

                if (!_mumbleClient.UseSyntheticSource)
                    SendAudioClip.GetData(newData.Pcm, _totalNumSamplesSent % NumSamplesInMicBuffer);
                else 
                    TestingClipToUse.GetData(newData.Pcm, _totalNumSamplesSent % TestingClipToUse.samples);
                //Debug.Log(Time.frameCount + " " + currentPosition);

                _totalNumSamplesSent += NumSamplesPerOutgoingPacket;

                if(VoiceSendingType == MicType.Amplitude)
                {
                    if (AmplitudeHigherThan(MinAmplitude, newData.Pcm))
                    {
                        _sampleNumberOfLastMinAmplitudeVoice = _totalNumSamplesSent;
                        if (OnMicData != null)
                            OnMicData(newData);
                        if (_writePositionalDataFunc != null)
                            _writePositionalDataFunc(ref newData.PositionalData, ref newData.PositionalDataLength);
                        else
                            newData.PositionalDataLength = 0;
                        _mumbleClient.SendVoicePacket(newData);
                    }
                    else
                    {
                        if (_totalNumSamplesSent > _sampleNumberOfLastMinAmplitudeVoice + _voiceHoldSamples)
                        {
                            newData.UnRef();
                            continue;
                        }
                        if (OnMicData != null)
                            OnMicData(newData);
                        if (_writePositionalDataFunc != null)
                            _writePositionalDataFunc(ref newData.PositionalData, ref newData.PositionalDataLength);
                        else
                            newData.PositionalDataLength = 0;
                        _mumbleClient.SendVoicePacket(newData);
                        // If this is the sample before the hold turns off, stop sending after it's sent
                        if (_totalNumSamplesSent + NumSamplesPerOutgoingPacket > _sampleNumberOfLastMinAmplitudeVoice + _voiceHoldSamples)
                            _mumbleClient.StopSendingVoice();
                    }
                }
                else
                {
                    if (OnMicData != null)
                        OnMicData(newData);
                    if (_writePositionalDataFunc != null)
                        _writePositionalDataFunc(ref newData.PositionalData, ref newData.PositionalDataLength);
                    else
                        newData.PositionalDataLength = 0;
                    _mumbleClient.SendVoicePacket(newData);
                }
            }
        }
        private static bool AmplitudeHigherThan(float minAmplitude, float[] pcm)
        {
            //return true;
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
        public MicType GetMicType(){
            return VoiceSendingType;
        }
        public bool HasMic()
        {
            return _currentMic != null;
        }
        public string GetCurrentMicName()
        {
            return _currentMic;
        }
        public void StartSendingAudio(int sampleRate)
        {
            if (_currentMic == null)
            {
                Debug.Log("Not sending audio, no current mic");
                return;
            }
            Debug.Log("Starting to send audio");
            SendAudioClip = Microphone.Start(_currentMic, true, NumRecordingSeconds, sampleRate);
            _previousPosition = 0;
            _numTimesLooped = 0;
            _totalNumSamplesSent = 0;
            _secondsWithoutMicSamples = 0;
            _sampleNumberOfLastMinAmplitudeVoice = int.MinValue;
            isRecording = true;
            _mumbleClient.SetSelfMute(false);
        }
        public void StopSendingAudio()
        {
            Debug.Log("Stopping sending audio");
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
                // TODO we should send one extra voice packet marked with isLast
                // Instead of sending an empty packet marked with isLast
                if (Input.GetKeyUp(PushToTalkKeycode))
                    StopSendingAudio();
            }
            if (isRecording)
                SendVoiceIfReady();
        }
    }
}
