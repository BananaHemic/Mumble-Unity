/* MumbleExamplePositionDisplay
 * This is an example of how a script could pull the position
 * From a MumbleAudioPlayer, with proper lerping
 */
using System;
using UnityEngine;

namespace Mumble.Sample
{
    [RequireComponent(typeof(MumbleAudioPlayer))]
    public class MumbleExamplePositionDisplay : MonoBehaviour
    {
        public MumbleAudioPlayer MumbleAudio;

        private bool ReadPositionalData(byte[] posData, out Vector3 pos)
        {
            if (posData == null)
            {
                pos = Vector3.zero;
                return false;
            }
            // This should NOT happen
            if (posData.Length != 3 * sizeof(float))
                Debug.LogError("Incorrect position size! " + posData.Length);

            int srcOffset = 0;
            pos.x = BitConverter.ToSingle(posData, srcOffset);
            srcOffset += sizeof(float);
            pos.y = BitConverter.ToSingle(posData, srcOffset);
            srcOffset += sizeof(float);
            pos.z = BitConverter.ToSingle(posData, srcOffset);
            return true;
        }

        void MoveSelfFromNetwork()
        {
            // Get the position data corresponding to before and after
            // so that we can lerp between the two samples
            bool didGet = MumbleAudio.GetPositionData(out byte[] dataA, out byte[] dataB, out float distAB);
            if (!didGet)
                return;

            // Turn the data into actual Vector3s
            // TODO we could cache the posA/B to avoid re-reading this positional data
            // every frame
            bool readA = ReadPositionalData(dataA, out Vector3 posA);
            bool readB = ReadPositionalData(dataB, out Vector3 posB);

            // Now set this GameObject's position accordingly
            if (readA && readB)
                transform.position = Vector3.Lerp(posA, posB, distAB);
            else if (readA && !readB)
                transform.position = posA;
            else if (!readA && readB)
                transform.position = posB;
            else
            {
                // This means we didn't read any position
                // you might want to handle this in different ways
            }
        }

        void Update()
        {
            MoveSelfFromNetwork();
        }
    }
}