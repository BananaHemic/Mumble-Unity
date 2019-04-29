/*
 * This is the front facing script to control how MumbleUnity works.
 * It's expected that, to fit in properly with your application,
 * You'll want to change this class (and possible SendMumbleAudio)
 * in order to work the way you want it to
 */
using UnityEngine;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using Mumble;

public class MumbleTester : MonoBehaviour {

    // Basic mumble audio player
    public GameObject MyMumbleAudioPlayerPrefab;
    // Mumble audio player that also receives position commands
    public GameObject MyMumbleAudioPlayerPositionedPrefab;

    public MumbleMicrophone MyMumbleMic;
    public DebugValues DebuggingVariables;

    private MumbleClient _mumbleClient;
    public bool ConnectAsyncronously = true;
    public bool SendPosition = false;
    public string HostName = "1.2.3.4";
    public int Port = 64738;
    public string Username = "ExampleUser";
    public string Password = "1passwordHere!";
    public string ChannelToJoin = "";

	void Start () {

        if(HostName == "1.2.3.4")
        {
            Debug.LogError("Please set the mumble host name to your mumble server");
            return;
        }
        
        if(HostName.ToLower() == "localhost") HostName = "127.0.0.1";

        Application.runInBackground = true;
        // If SendPosition, we'll send three floats.
        // This is roughly the standard for Mumble, however it seems that
        // Murmur supports more
        int posLength = SendPosition ? 3 * sizeof(float) : 0;
        _mumbleClient = new MumbleClient(HostName, Port, CreateMumbleAudioPlayerFromPrefab,
            DestroyMumbleAudioPlayer, OnOtherUserStateChange, ConnectAsyncronously,
            SpeakerCreationMode.ALL, DebuggingVariables, posLength);

        if (DebuggingVariables.UseRandomUsername)
            Username += UnityEngine.Random.Range(0, 100f);

        if (ConnectAsyncronously)
            StartCoroutine(ConnectAsync());
        else
        {
            _mumbleClient.Connect(Username, Password);
            if(MyMumbleMic != null)
            {
                _mumbleClient.AddMumbleMic(MyMumbleMic);
                if (SendPosition)
                    MyMumbleMic.SetPositionalDataFunction(WritePositionalData);
            }
        }

#if UNITY_EDITOR
        if (DebuggingVariables.EnableEditorIOGraph)
        {
            EditorGraph editorGraph = EditorWindow.GetWindow<EditorGraph>();
            editorGraph.Show();
            StartCoroutine(UpdateEditorGraph());
        }
#endif
    }
    /// <summary>
    /// An example of how to serialize the positional data that you're interested in
    /// NOTE: this function, in the current implementation, is called regardless
    /// of if the user is speaking
    /// </summary>
    /// <param name="posData"></param>
    private void WritePositionalData(ref byte[] posData, ref int posDataLength)
    {
        // Get the XYZ position of the camera
        Vector3 pos = Camera.main.transform.position;
        //Debug.Log("Sending pos: " + pos);
        // Copy the XYZ floats into our positional array
        int dstOffset = 0;
        Buffer.BlockCopy(BitConverter.GetBytes(pos.x), 0, posData, dstOffset, sizeof(float));
        dstOffset += sizeof(float);
        Buffer.BlockCopy(BitConverter.GetBytes(pos.y), 0, posData, dstOffset, sizeof(float));
        dstOffset += sizeof(float);
        Buffer.BlockCopy(BitConverter.GetBytes(pos.z), 0, posData, dstOffset, sizeof(float));

        posDataLength = 3 * sizeof(float);
        // The reverse method is in MumbleExamplePositionDisplay
    }
    IEnumerator ConnectAsync()
    {
        while (!_mumbleClient.ReadyToConnect)
            yield return null;
        Debug.Log("Will now connect");
        _mumbleClient.Connect(Username, Password);
        yield return null;
        if(MyMumbleMic != null)
        {
            _mumbleClient.AddMumbleMic(MyMumbleMic);
            if (SendPosition)
                MyMumbleMic.SetPositionalDataFunction(WritePositionalData);
        }
    }
    private MumbleAudioPlayer CreateMumbleAudioPlayerFromPrefab(string username, uint session)
    {
        // Depending on your use case, you might want to add the prefab to an existing object (like someone's head)
        // If you have users entering and leaving frequently, you might want to implement an object pool
        GameObject newObj = SendPosition
            ? GameObject.Instantiate(MyMumbleAudioPlayerPositionedPrefab)
            : GameObject.Instantiate(MyMumbleAudioPlayerPrefab);

        newObj.name = username + "_MumbleAudioPlayer";
        MumbleAudioPlayer newPlayer = newObj.GetComponent<MumbleAudioPlayer>();
        Debug.Log("Adding audio player for: " + username);
        return newPlayer;
    }
    private void OnOtherUserStateChange(uint session, MumbleProto.UserState updatedDeltaState, MumbleProto.UserState fullUserState)
    {
        print("User #" + session + " had their user state change");
        // Here we can do stuff like update a UI with users' current channel/mute etc.
    }
    private void DestroyMumbleAudioPlayer(uint session, MumbleAudioPlayer playerToDestroy)
    {
        UnityEngine.GameObject.Destroy(playerToDestroy.gameObject);
    }
    void OnApplicationQuit()
    {
        Debug.LogWarning("Shutting down connections");
        if(_mumbleClient != null)
            _mumbleClient.Close();
    }
    IEnumerator UpdateEditorGraph()
    {
        long numPacketsReceived = 0;
        long numPacketsSent = 0;
        long numPacketsLost = 0;

        while (true)
        {
            yield return new WaitForSeconds(0.1f);

            long numSentThisSample = _mumbleClient.NumUDPPacketsSent - numPacketsSent;
            long numRecvThisSample = _mumbleClient.NumUDPPacketsReceieved - numPacketsReceived;
            long numLostThisSample = _mumbleClient.NumUDPPacketsLost - numPacketsLost;

            Graph.channel[0].Feed(-numSentThisSample);//gray
            Graph.channel[1].Feed(-numRecvThisSample);//blue
            Graph.channel[2].Feed(-numLostThisSample);//red

            numPacketsSent += numSentThisSample;
            numPacketsReceived += numRecvThisSample;
            numPacketsLost += numLostThisSample;
        }
    }
	void Update () {
        if (!_mumbleClient.ReadyToConnect)
            return;
        if (Input.GetKeyDown(KeyCode.S))
        {
            _mumbleClient.SendTextMessage("This is an example message from Unity");
            print("Sent mumble message");
        }
        if (Input.GetKeyDown(KeyCode.J))
        {
            print("Will attempt to join channel " + ChannelToJoin);
            _mumbleClient.JoinChannel(ChannelToJoin);
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            print("Will join root");
            _mumbleClient.JoinChannel("Root");
        }
	}
}
