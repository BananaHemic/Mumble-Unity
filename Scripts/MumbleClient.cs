using System;
using System.Net;
using MumbleProto;
using Version = MumbleProto.Version;
using UnityEngine;
using System.Collections.Generic;

namespace Mumble
{
    public delegate void UpdateOcbServerNonce(byte[] cryptSetup);
    [Serializable]
    public class DebugValues {
        [Header("Whether to add a random number to the end of the username")]
        public bool UseRandomUsername;
        [Header("Whether to stream the audio back to Unity directly")]
        public bool UseLocalLoopback;
        [Header("Whether to use a synthetic audio source of an audio sample")]
        public bool UseSyntheticSource;
        [Header("Create a graph in the editor displaying the IO")]
        public bool EnableEditorIOGraph;
    }

    public class MumbleClient
    {
        // This sets if we're going to listen to our own audio
        public bool UseLocalLoopBack { get { return _debugValues.UseLocalLoopback; } }
        // This sets if we send synthetic audio instead of a mic audio
        public bool UseSyntheticSource { get { return _debugValues.UseSyntheticSource; } }
        public int NumUDPPacketsSent { get { return _udpConnection.NumPacketsSent; } }
        public int NumUDPPacketsReceieved { get { return _udpConnection.NumPacketsRecv; } }
        public long NumUDPPacketsLost { get { return _audioDecodingBuffer.NumPacketsLost; } }
        public bool ConnectionSetupFinished { get; internal set; }

        private MumbleTcpConnection _tcpConnection;
        private MumbleUdpConnection _udpConnection;
        private ManageAudioSendBuffer _manageSendBuffer;
        private MumbleMicrophone _mumbleMic;
        private MumbleAudioPlayer _mumbleAudioPlayer;

        private DebugValues _debugValues;
        private Dictionary<uint, UserState> AllUsers = new Dictionary<uint, UserState>();
        private Dictionary<string, ChannelState> Channels = new Dictionary<string, ChannelState>();
        private readonly AudioDecodingBuffer _audioDecodingBuffer;

        internal Version RemoteVersion { get; set; }
        internal CryptSetup CryptSetup { get; set; }
        internal UserState OurUserState { get; set; }
        internal ServerSync ServerSync { get; set; }
        internal CodecVersion CodecVersion { get; set; }
        internal PermissionQuery PermissionQuery { get; set; }
        internal ServerConfig ServerConfig { get; set; }

        internal int EncoderSampleRate { get; private set; }
        internal int NumSamplesPerOutgoingPacket { get; private set; }

        private OpusCodec _codec;

        //The Mumble version of this integration
        public const string ReleaseName = "MumbleUnity";
        public const uint Major = 1;
        public const uint Minor = 2;
        public const uint Patch = 8;

        public MumbleClient(string hostName, int port, DebugValues debugVals=null)
        {
            IPAddress[] addresses = Dns.GetHostAddresses(hostName);
            if (addresses.Length == 0)
            {
                throw new ArgumentException(
                    "Unable to retrieve address from specified host name.",
                    hostName
                    );
            }
            var host = new IPEndPoint(addresses[0], port);
            _udpConnection = new MumbleUdpConnection(host, this);
            _tcpConnection = new MumbleTcpConnection(host, hostName, _udpConnection.UpdateOcbServerNonce, _udpConnection, this);

            if (debugVals == null)
                debugVals = new DebugValues();
            _debugValues = debugVals;

            //Maybe do Lazy?
            _codec = new OpusCodec();

            _manageSendBuffer = new ManageAudioSendBuffer(_codec, _udpConnection, this);
            _audioDecodingBuffer = new AudioDecodingBuffer(_codec);
        }
        internal void AddMumbleMic(MumbleMicrophone newMic)
        {
            _mumbleMic = newMic;
            _mumbleMic.Initialize(this);
            EncoderSampleRate = _mumbleMic.GetCurrentMicSampleRate();

            if (EncoderSampleRate == -1)
                return;
            
            NumSamplesPerOutgoingPacket = MumbleConstants.NUM_FRAMES_PER_OUTGOING_PACKET * EncoderSampleRate / 100;

            _codec.InitializeEncoderWithSampleRate(EncoderSampleRate);
        }
        internal void AddMumbleAudioPlayer(MumbleAudioPlayer newSpeaker)
        {
            _mumbleAudioPlayer = newSpeaker;
            _mumbleAudioPlayer.Initialize(this);
        }
        internal PcmArray GetAvailablePcmArray()
        {
            return _manageSendBuffer.GetAvailablePcmArray();
        }
        internal void AddUser(UserState newUserState)
        {
            AllUsers.Add(newUserState.session, newUserState);
        }
        internal void RemoveUser(uint removedUserSession)
        {
            AllUsers.Remove(removedUserSession);
        }
        public void Connect(string username, string password)
        {
            _tcpConnection.StartClient(username, password);
        }

        internal void ConnectUdp()
        {
            _udpConnection.Connect();
        }
        public void Close()
        {
            _tcpConnection.Close();
            _udpConnection.Close();
            _manageSendBuffer.Dispose();
            _manageSendBuffer = null;
        }

        public void SendTextMessage(string textMessage)
        {
            var msg = new TextMessage
            {
                message = textMessage,
            };
            msg.channel_id.Add(OurUserState.channel_id);
            msg.actor = ServerSync.session;
            Debug.Log("Now session length = " + msg.session.Count);

            _tcpConnection.SendMessage(MessageType.TextMessage, msg);
        }
        public void SendVoicePacket(PcmArray floatData)
        {
            _manageSendBuffer.SendVoice(floatData, SpeechTarget.Normal, 0);
        }
        public void ReceiveEncodedVoice(byte[] data, long sequence)
        {
            //Debug.Log("Adding packet");
            _audioDecodingBuffer.AddEncodedPacket(sequence, data);
        }
        public void LoadArrayWithVoiceData(float[] pcmArray, int offset, int length)
        {
            _audioDecodingBuffer.Read(pcmArray, offset, length);
        }
        public void JoinChannel(string channelToJoin)
        {
            ChannelState channel;
            if (!Channels.TryGetValue(channelToJoin, out channel))
            {
                Debug.LogError("Channel " + channelToJoin + " not found!");
                return;
            }
            UserState state = new UserState();
            state.channel_id = channel.channel_id;
            state.actor = OurUserState.actor;
            state.session = OurUserState.session; 
            _tcpConnection.SendMessage<MumbleProto.UserState>(MessageType.UserState, state);
        }
        internal void AddChannel(ChannelState channelToAdd)
        {
            Channels[channelToAdd.name] = channelToAdd;
        }
        internal void RemoveChannel(ChannelState channelToRemove)
        {
            Channels.Remove(channelToRemove.name);
        }
        /// <summary>
        /// Tell the encoder to send the last audio packet, then reset the sequence number
        /// </summary>
        public void StopSendingVoice()
        {
            _manageSendBuffer.SendVoiceStopSignal();
        }
        public byte[] GetLatestClientNonce()
        {
            return _udpConnection.GetLatestClientNonce();
        }
        public static int GetNearestSupportedSampleRate(int listedRate)
        {
            int currentBest = -1;
            int currentDifference = int.MaxValue;

            for(int i = 0; i < MumbleConstants.SUPPORTED_SAMPLE_RATES.Length; i++)
            {
                if(Math.Abs(listedRate - MumbleConstants.SUPPORTED_SAMPLE_RATES[i]) < currentDifference)
                {
                    currentBest = MumbleConstants.SUPPORTED_SAMPLE_RATES[i];
                    currentDifference = Math.Abs(listedRate - MumbleConstants.SUPPORTED_SAMPLE_RATES[i]);
                }
            }

            return currentBest;
        }
    }
}