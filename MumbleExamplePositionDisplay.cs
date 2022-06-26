/* MumbleExamplePositionDisplay
 * This is an example of how a script could pull the position
 * From a MumbleAudioPlayer, with proper lerping
 */
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

[RequireComponent(typeof(Mumble.MumbleAudioPlayer))]
public class MumbleExamplePositionDisplay : MonoBehaviour {

    public Mumble.MumbleAudioPlayer MumbleAudio;

	void Start () {
		MumbleAudio = gameObject.GetComponent<Mumble.MumbleAudioPlayer>();
	}
    private bool ReadPositionalData(byte[] posData, out Vector3 pos)
    {
        if(posData == null)
        {
            pos = Vector3.zero;
            return false;
        }
        // This should NOT happen
        if (posData.Length != 3 * sizeof(float))
            Debug.LogError("Incorrect position size! " + posData.Length);

        //Debug.Log(posData[0] + ", " + posData[1]);

        int srcOffset = 0;
        pos.x = BitConverter.ToSingle(posData, srcOffset);
        //Debug.Log(pos.x);
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
        byte[] dataA;
        byte[] dataB;
        float distAB;
        bool didGet = MumbleAudio.GetPositionData(out dataA, out dataB, out distAB);
        if (!didGet)
            return;
        //Debug.Log("A: " + (dataA == null ? "null" : dataA.Length.ToString())
            //+ "B: " + (dataB == null ? "null" : dataB.Length.ToString()));

        // Turn the data into actual Vector3s
        //TODO we could cache the posA/B to avoid re-reading this positional data
        // every frame
        Vector3 posA;
        Vector3 posB;
        bool readA = ReadPositionalData(dataA, out posA);
        bool readB = ReadPositionalData(dataB, out posB);

        //Debug.Log("Read A: " + readA + " readB: " + readB);

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
        //Debug.Log(transform.position);
    }
	
	void Update () {
        MoveSelfFromNetwork();
	}
}
