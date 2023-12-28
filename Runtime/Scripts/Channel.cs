using MumbleProto;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mumble
{
    public class Channel
    {
        public string Name
        {
            get
            {
                return _channelState.Name;
            }
        }
        public uint ChannelId
        {
            get
            {
                return _channelState.ChannelId;
            }
        }
        public uint[] Links { get { return _channelState.Links; } }
        private readonly ChannelState _channelState;
        // The audio channels that we shared audio with
        // TODO we can get a faster data structure here
        private readonly List<Channel> _sharedAudioChannels;
        private readonly object _lock = new();

        internal Channel(ChannelState initialState)
        {
            _channelState = initialState;
            _sharedAudioChannels = new List<Channel>();

            // Link updates happen in a sorta weird way
            UpdateLinks(initialState.LinksAdds, initialState.LinksRemoves);
        }

        public bool DoesShareAudio(Channel other)
        {
            lock (_lock)
            {
                return other.ChannelId == ChannelId
                    || _sharedAudioChannels.Contains(other);
            }
        }

        public string Links2String()
        {
            StringBuilder sb = new();
            sb.Append("[");
            if (_channelState.Links != null)
            {
                for (int i = 0; i < _channelState.Links.Length; i++)
                {
                    sb.Append(_channelState.Links[i]);
                    if (i < _channelState.Links.Length - 1)
                        sb.Append(",");
                }
            }
            sb.Append("]");

            return sb.ToString();
        }

        internal void UpdateSharedAudioChannels(Dictionary<uint, Channel> Channels)
        {
            lock (_lock)
            {
                _sharedAudioChannels.Clear();

                if (_channelState.Links == null
                    || _channelState.Links.Length == 0)
                    return;

                // We can use a faster data structure here
                List<uint> checkedChannels = new();

                Stack<Channel> channelsToCheck = new();
                channelsToCheck.Push(this);

                while (channelsToCheck.Count > 0)
                {
                    Channel chan = channelsToCheck.Pop();
                    checkedChannels.Add(chan.ChannelId);

                    if (chan.Links == null || chan.Links.Length == 0)
                        continue;
                    // Iterate through all links, making sure not to re-check
                    // Already inspected channels
                    for (int i = 0; i < chan.Links.Length; i++)
                    {
                        uint val = chan.Links[i];
                        if (checkedChannels.Contains(val))
                            continue;

                        if (!Channels.TryGetValue(val, out Channel linkedChan))
                            continue;
                        _sharedAudioChannels.Add(linkedChan);
                        channelsToCheck.Push(linkedChan);
                    }
                }
            }
        }

        void UpdateLinks(uint[] addedLinks, uint[] removedLinks)
        {
            // If we have no current links, then we just use the new links
            if (_channelState.Links == null || _channelState.Links.Length == 0)
            {
                _channelState.Links = addedLinks;
                return;
            }

            // Get the updated number of links to add
            int newNumLinks = _channelState.Links.Length
                + (addedLinks == null ? 0 : addedLinks.Length)
                - (removedLinks == null ? 0 : removedLinks.Length);

            uint[] oldLinks = _channelState.Links;
            _channelState.Links = new uint[newNumLinks];

            if (newNumLinks == 0)
                return;

            int dstIdx = 0;
            // First add the old links
            if (removedLinks == null || removedLinks.Length == 0)
            {
                Array.Copy(oldLinks, _channelState.Links, oldLinks.Length);
                dstIdx = oldLinks.Length;
            }
            else
            {
                for (int i = 0; i < oldLinks.Length; i++)
                {
                    uint val = oldLinks[i];
                    // Don't add links that were removed
                    if (Array.IndexOf(removedLinks, val) < 0)
                        continue;
                    _channelState.Links[dstIdx] = oldLinks[i];
                    dstIdx++;
                }
            }

            // Now add all the new links
            if (addedLinks != null)
                Array.Copy(addedLinks, 0, _channelState.Links, dstIdx, addedLinks.Length);
        }

        internal void UpdateFromState(ChannelState deltaState)
        {
            if (deltaState.ShouldSerializeParent())
                _channelState.Parent = deltaState.Parent;
            if (deltaState.ShouldSerializeDescription())
                _channelState.Description = deltaState.Description;
            if (deltaState.ShouldSerializeName())
                _channelState.Name = deltaState.Name;
            if (deltaState.ShouldSerializeDescriptionHash())
                _channelState.DescriptionHash = deltaState.DescriptionHash;
            if (deltaState.ShouldSerializeMaxUsers())
                _channelState.MaxUsers = deltaState.MaxUsers;
            if (deltaState.ShouldSerializePosition())
                _channelState.Position = deltaState.Position;

            // Link updates happen in a sorta weird way
            if (deltaState.Links != null)
                _channelState.Links = deltaState.Links;
            UpdateLinks(deltaState.LinksAdds, deltaState.LinksRemoves);
        }
    }
}
