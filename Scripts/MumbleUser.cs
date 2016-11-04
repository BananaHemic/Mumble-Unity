using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mumble
{
        public class User: IEquatable<User>
    {
        private readonly OpusCodec _codec;

        public UInt32 Id { get; private set; }
        public bool Deaf { get; set; }
        public bool Muted { get; set; }
        public bool SelfDeaf { get; set; }
        public bool SelfMuted { get; set; }
        public bool Suppress { get; set; }

        /*
        private Channel _channel;
        public Channel Channel
        {
            get { return _channel; }
            set
            {
                if (_channel != null)
                    _channel.RemoveUser(this);

                _channel = value;

                if (value != null)
                    value.AddUser(this);
            }
        }
        */

        public string Name { get; set; }
        public string Comment { get; set; }

        public User(uint id, OpusCodec codec)
        {
            _codec = codec;
            Id = id;
            _buffer = new AudioDecodingBuffer(_codec);
        }

        private static readonly string[] _split = { "\r\n", "\n" };

        /// <summary>
        /// Send a text message
        /// </summary>
        /// <param name="message">A text message (which will be split on newline characters)</param>
        public void SendMessage(string message)
        {
            var messages = message.Split(_split, StringSplitOptions.None);
            SendMessage(messages);
        }

        /// <summary>
        /// Send a text message
        /// </summary>
        /// <param name="message">Individual lines of a text message</param>
        public void SendMessage(string[] message)
        {
            //TODO
            /*
            _owner.Connection.SendControl<TextMessage>(PacketType.TextMessage, new TextMessage
            {
                actor = _owner.LocalUser.Id,
                message = string.Join(Environment.NewLine, message),
            });
            */
        }

        /*
        /// <summary>
        /// Move user to a channel
        /// </summary>
        /// <param name="channel">Channel to move to</param>
        public void Move(Channel channel)
        {
            if (_channel == channel)
                return;

            UserState userstate = new UserState();
            userstate.actor = Id;
            userstate.channel_id = channel.Id;

            _owner.Connection.SendControl<UserState>(PacketType.UserState, userstate);
        }

        protected internal IVoiceCodec GetCodec(SpeechCodecs codec)
        {
            return _codecs.GetCodec(codec);
        }
        */
        public override string ToString()
        {
            return Name;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var u = obj as User;
            if (u != null)
                return (Equals(u));

            return ReferenceEquals(this, obj);
        }

        public bool Equals(User other)
        {
            return other.Id == Id;
        }

        private readonly AudioDecodingBuffer _buffer;
        public AudioDecodingBuffer Voice
        {
            get
            {
                return _buffer;
            }
        }

        public void ReceiveEncodedVoice(byte[] data, long sequence)
        {
            _buffer.AddEncodedPacket(sequence, data);
        }
    }
}
