/*
 * This is the front facing script to control how MumbleUnity works.
 * It's expected that, to fit in properly with your application,
 * You'll want to change this class (and possible SendMumbleAudio)
 * in order to work the way you want it to
 */
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using Mumble;

public class MumbleTester : MonoBehaviour {

    public MumbleAudioPlayer MyMumbleAudioPlayer;
    public MumbleMicrophone MyMumbleMic;
    public DebugValues DebuggingVariables;

    private MumbleClient _mumbleClient;
    public string HostName = "1.2.3.4";
    public int Port = 64738;
    public string Username = "ExampleUser";
    public string Password = "1passwordHere!";

	void Start () {

        if(HostName == "1.2.3.4")
        {
            Debug.LogError("Please set the mumble host name to your mumble server");
            return;
        }

        _mumbleClient = new MumbleClient(HostName, Port, DebuggingVariables);

        if (DebuggingVariables.UseRandomUsername)
            Username += Random.Range(0, 100f);
        _mumbleClient.Connect(Username, Password);

        if(MyMumbleAudioPlayer != null)
            _mumbleClient.AddMumbleAudioPlayer(MyMumbleAudioPlayer);
        if(MyMumbleMic != null)
            _mumbleClient.AddMumbleMic(MyMumbleMic);

#if UNITY_EDITOR
        if (DebuggingVariables.EnableEditorIOGraph)
        {
            EditorGraph editorGraph = EditorWindow.GetWindow<EditorGraph>();
            editorGraph.Show();
            StartCoroutine(UpdateEditorGraph());
        }
#endif
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

            Graph.channel[0].Feed(-numSentThisSample);
            Graph.channel[1].Feed(-numRecvThisSample);
            Graph.channel[2].Feed(-numLostThisSample);

            numPacketsSent += numSentThisSample;
            numPacketsReceived += numRecvThisSample;
            numPacketsLost += numLostThisSample;
        }
    }
	void Update () {
        if (Input.GetKeyDown(KeyCode.S))
        {
            _mumbleClient.SendTextMessage("This is an example message from Unity");
            print("Sent mumble message");
        }
	}
}
