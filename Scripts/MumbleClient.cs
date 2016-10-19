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
        private MumbleTcpConnection _mtc;
        private MumbleUdpConnection _muc;
        private ManageAudioSendBuffer _manageSendBuffer;
        private User oneUser;

        public bool ConnectionSetupFinished { get; internal set; }

        internal Version RemoteVersion { get; set; }
        internal CryptSetup CryptSetup { get; set; }
        internal ChannelState ChannelState { get; set; }
        internal UserState UserState { get; set; }
        internal ServerSync ServerSync { get; set; }
        internal CodecVersion CodecVersion { get; set; }
        internal PermissionQuery PermissionQuery { get; set; }
        internal ServerConfig ServerConfig { get; set; }

        private OpusCodec _codec;

        public int NumSamplesPerFrame
        {
            get
            {
                return _codec.PermittedEncodingFrameSizes.ElementAt(0);
            }
        }

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
//            logger.Debug("Connecting via TCP");
            _mtc.StartClient(username, password);
        }

        internal void ConnectUdp()
        {
//            logger.Debug("Connecting via UDP");
            _muc.Connect();
        }
        public void Close()
        {
            //TODO actually tell the server we're not online, right now we rely on the connection expiring
            _mtc.Close();
            _muc.Close();
            _manageSendBuffer.SendVoiceStop();
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
            _manageSendBuffer.SendVoice(new ArraySegment<byte>(PcmUtils.Raw2Pcm(floatData)), SpeechTarget.Normal, 0);
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