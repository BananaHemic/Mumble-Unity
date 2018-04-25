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

    public GameObject MyMumbleAudioPlayerPrefab;
    public MumbleMicrophone MyMumbleMic;
    public DebugValues DebuggingVariables;

    private MumbleClient _mumbleClient;
    public bool ConnectAsyncronously = true;
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
        Application.runInBackground = true;
        _mumbleClient = new MumbleClient(HostName, Port, CreateMumbleAudioPlayerFromPrefab, DestroyMumbleAudioPlayer, ConnectAsyncronously, DebuggingVariables);

        if (DebuggingVariables.UseRandomUsername)
            Username += UnityEngine.Random.Range(0, 100f);

        if (ConnectAsyncronously)
            StartCoroutine(ConnectAsync());
        else
        {
            _mumbleClient.Connect(Username, Password);
            if(MyMumbleMic != null)
                _mumbleClient.AddMumbleMic(MyMumbleMic);
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
    IEnumerator ConnectAsync()
    {
        while (!_mumbleClient.ReadyToConnect)
            yield return null;
        Debug.Log("Will now connect");
        _mumbleClient.Connect(Username, Password);
        yield return null;
        if(MyMumbleMic != null)
            _mumbleClient.AddMumbleMic(MyMumbleMic);
    }
    private MumbleAudioPlayer CreateMumbleAudioPlayerFromPrefab(string username, uint session)
    {
        // Depending on your use case, you might want to add the prefab to an existing object (like someone's head)
        // If you have users entering and leaving frequently, you might want to implement an object pool
        GameObject newObj = GameObject.Instantiate(MyMumbleAudioPlayerPrefab);
        newObj.name = username + "_MumbleAudioPlayer";
        MumbleAudioPlayer newPlayer = newObj.GetComponent<MumbleAudioPlayer>();
        Debug.Log("Adding audio player for: " + username);
        return newPlayer;
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
	}
}
