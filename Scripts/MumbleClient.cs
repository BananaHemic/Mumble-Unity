using System;
using System.Net;
using MumbleProto;
using Version = MumbleProto.Version;
using UnityEngine;
using System.Linq;
using System.Threading;

namespace Mumble
{
    public delegate void MumbleError(string message, bool fatal = false);
    public delegate void UpdateOcbServerNonce(byte[] cryptSetup);

    public class MumbleClient
    {
        /// This sets if we're going to listen to our own audio
        public static readonly bool UseLocalLoopBack = false;
        // This sets if we send synthetic audio instead of a mic audio
        public static readonly bool UseSyntheticMic = false;

        public bool ConnectionSetupFinished { get; internal set; }


        private MumbleTcpConnection _mtc;
        private MumbleUdpConnection _muc;
        private ManageAudioSendBuffer _manageSendBuffer;
        private User oneUser;


        internal Version RemoteVersion { get; set; }
        internal CryptSetup CryptSetup { get; set; }
        internal ChannelState ChannelState { get; set; }
        internal UserState UserState { get; set; }
        internal ServerSync ServerSync { get; set; }
        internal CodecVersion CodecVersion { get; set; }
        internal PermissionQuery PermissionQuery { get; set; }
        internal ServerConfig ServerConfig { get; set; }

        private OpusCodec _codec;

        public int NumSamplesPerFrame { get; private set; }

        //The Mumble version of this integration
        public const string ReleaseName = "MumbleUnity";
        public const uint Major = 1;
        public const uint Minor = 2;
        public const uint Patch = 8;

        public MumbleClient(String hostName, int port)
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
            _muc = new MumbleUdpConnection(host, DealWithError, this);
            _mtc = new MumbleTcpConnection(host, hostName, _muc.UpdateOcbServerNonce, DealWithError, _muc, this);

            //Maybe do Lazy?
            _codec = new OpusCodec();
            //Use 20ms samples
            NumSamplesPerFrame = _codec.PermittedEncodingFrameSizes.ElementAt(_codec.PermittedEncodingFrameSizes.Count() - 4);

            oneUser = new User(17, _codec);
            _manageSendBuffer = new ManageAudioSendBuffer(_codec, _muc);
        }

        private void DealWithError(string message, bool fatal)
        {
            if (fatal)
            {
                Console.WriteLine("Fatal error: " + message);
                Console.ReadLine();
                _mtc.Close();
                _muc.Close();
                Environment.Exit(1);
            }
            else
            {
                Console.WriteLine("Recovering from: " + message);
            }
        }

        public void Connect(string username, string password)
        {
            _mtc.StartClient(username, password);
        }

        internal void ConnectUdp()
        {
            _muc.Connect();
        }
        public void Close()
        {
            _mtc.Close();
            _muc.Close();
            _manageSendBuffer.Dispose();
            Debug.Log("Closing all connections");
        }

        public void SendTextMessage(string textMessage)
        {
            var msg = new TextMessage
            {
                message = textMessage,
            };
            //msg.session.Add(ServerSync.session);
            msg.channel_id.Add(ChannelState.channel_id);
            msg.actor = ServerSync.session;
            Debug.Log("Now session length = " + msg.session.Count);

            _mtc.SendMessage(MessageType.TextMessage, msg);
        }
        public void SendVoicePacket(float[] floatData)
        {
            _manageSendBuffer.SendVoice(floatData, SpeechTarget.Normal, 0);
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
            return _muc.GetLatestClientNonce();
        }
        public User GetUserAtTarget(int target)
        {
            return oneUser;
        }
    }
}