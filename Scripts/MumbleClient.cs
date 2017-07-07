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
        public long NumUDPPacketsLost {
            get {
                long numLost = 0;
                foreach (var pair in _audioDecodingBuffers)
                    numLost += pair.Value.NumPacketsLost;
                return numLost;
            }
        }
        public bool ConnectionSetupFinished { get; internal set; }
        /// <summary>
        /// Methods to create or delete the Unity audio players
        /// These are NOT! Multithread safe, as they can call Unity functions
        /// </summary>
        /// <returns></returns>
        public delegate MumbleAudioPlayer AudioPlayerCreatorMethod();
        public delegate void AudioPlayerRemoverMethod(MumbleAudioPlayer audioPlayerToRemove);

        private readonly MumbleTcpConnection _tcpConnection;
        private readonly MumbleUdpConnection _udpConnection;
        private readonly ManageAudioSendBuffer _manageSendBuffer;
        private MumbleMicrophone _mumbleMic;
        private readonly AudioPlayerCreatorMethod _audioPlayerCreator;
        private readonly AudioPlayerRemoverMethod _audioPlayerDestroyer;

        private DebugValues _debugValues;
        private readonly Dictionary<uint, UserState> AllUsers = new Dictionary<uint, UserState>();
        private readonly Dictionary<uint, ChannelState> Channels = new Dictionary<uint, ChannelState>();
        private readonly Dictionary<UInt32, AudioDecodingBuffer> _audioDecodingBuffers = new Dictionary<uint, AudioDecodingBuffer>();
        private readonly Dictionary<UInt32, MumbleAudioPlayer> _mumbleAudioPlayers = new Dictionary<uint, MumbleAudioPlayer>();

        internal UserState OurUserState { get; private set; }
        internal Version RemoteVersion { get; set; }
        internal CryptSetup CryptSetup { get; set; }
        internal ServerSync ServerSync { get; private set; }
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

        public MumbleClient(string hostName, int port, AudioPlayerCreatorMethod createMumbleAudioPlayerMethod, AudioPlayerRemoverMethod removeMumbleAudioPlayerMethod, DebugValues debugVals=null)
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
            _udpConnection.SetTcpConnection(_tcpConnection);
            _audioPlayerCreator = createMumbleAudioPlayerMethod;
            _audioPlayerDestroyer = removeMumbleAudioPlayerMethod;

            if (debugVals == null)
                debugVals = new DebugValues();
            _debugValues = debugVals;

            //Maybe do Lazy?
            _codec = new OpusCodec();

            _manageSendBuffer = new ManageAudioSendBuffer(_codec, _udpConnection, this);
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
        internal PcmArray GetAvailablePcmArray()
        {
            return _manageSendBuffer.GetAvailablePcmArray();
        }
        internal void AddUser(UserState newUserState)
        {
            if (!AllUsers.ContainsKey(newUserState.session))
            {
                AllUsers[newUserState.session] = newUserState;
                AudioDecodingBuffer buffer = new AudioDecodingBuffer(_codec);
                _audioDecodingBuffers.Add(newUserState.session, buffer);
                EventProcessor.Instance.QueueEvent(() =>
                {
                    // We also create a new audio player for each user
                    MumbleAudioPlayer newPlayer = _audioPlayerCreator();
                    _mumbleAudioPlayers.Add(newUserState.session, newPlayer);
                    newPlayer.Initialize(this, newUserState.session);
                });
            }
            else
            {
                // Copy over the things that have changed
                //TODO we should be doing with with a proto merge in MumbleTCPConnection,
                //but I don't know how to identify object it needs to be merged with before it's been deserialized
                if(!string.IsNullOrEmpty(newUserState.name))
                    AllUsers[newUserState.session].name = newUserState.name;
                AllUsers[newUserState.session].channel_id = newUserState.channel_id;
            }
        }
        internal void SetServerSync(ServerSync sync)
        {
            ServerSync = sync;
            OurUserState = AllUsers[ServerSync.session];
        }
        internal void RemoveUser(uint removedUserSession)
        {
            AllUsers.Remove(removedUserSession);
            // Try to remove the audio player if it exists
            MumbleAudioPlayer oldAudioPlayer;
            if(_mumbleAudioPlayers.TryGetValue(removedUserSession, out oldAudioPlayer))
            {
                _mumbleAudioPlayers.Remove(removedUserSession);
                EventProcessor.Instance.QueueEvent(() =>
                {
                    _audioPlayerDestroyer(oldAudioPlayer);
                });
            }
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
        public void ReceiveEncodedVoice(UInt32 session, byte[] data, long sequence)
        {
            //Debug.Log("Adding packet");
            _audioDecodingBuffers[session].AddEncodedPacket(sequence, data);
        }
        public void LoadArrayWithVoiceData(UInt32 session, float[] pcmArray, int offset, int length)
        {
            if (session == ServerSync.session)
                return;
            //Debug.Log("Will decode for " + session);

            _audioDecodingBuffers[session].Read(pcmArray, offset, length);
        }
        public void JoinChannel(string channelToJoin)
        {
            ChannelState channel;
            if (!TryGetChannelByName(channelToJoin, out channel))
            {
                Debug.LogError("Channel " + channelToJoin + " not found!");
                return;
            }
            UserState state = new UserState();
            state.channel_id = channel.channel_id;
            state.actor = OurUserState.session;
            state.session = OurUserState.session;
            Debug.Log("Attempting to join channel Id: " + state.channel_id);
            _tcpConnection.SendMessage<MumbleProto.UserState>(MessageType.UserState, state);
        }
        private bool TryGetChannelByName(string channelName, out ChannelState channelState)
        {
            foreach(uint key in Channels.Keys)
            {
                if (Channels[key].name == channelName)
                {
                    channelState = Channels[key];
                    return true;
                }
                //Debug.Log("Not " + Channels[key].name + " == " + channelName);
            }
            channelState = null;
            return false;
        }
        public string GetCurrentChannel()
        {
            ChannelState ourChannel;
            if(Channels.TryGetValue(OurUserState.channel_id, out ourChannel))
                return ourChannel.name;

            Debug.LogError("Could not get current channel");
            return null;
        }
        internal void AddChannel(ChannelState channelToAdd)
        {
            // If the channel already exists, just copy over the non-null data
            if (Channels.ContainsKey(channelToAdd.channel_id))
            {
                ChannelState previousChannelState = Channels[channelToAdd.channel_id];
                if (string.IsNullOrEmpty(channelToAdd.name))
                    channelToAdd.name = previousChannelState.name;
                if (string.IsNullOrEmpty(channelToAdd.description))
                    channelToAdd.description = previousChannelState.description;
            }
            Channels[channelToAdd.channel_id] = channelToAdd;
        }
        internal void RemoveChannel(uint channelIdToRemove)
        {
            if (channelIdToRemove == OurUserState.channel_id)
                Debug.LogWarning("Removed current channel");
            Channels.Remove(channelIdToRemove);
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