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
    public SendMumbleAudio MyMumbleAudioSender;
    public DebugValues DebuggingVariables;

    private MumbleClient _mumbleClient;
    public string hostName = "1.2.3.4";
    public int port = 64738;
    public string _username = "SuperUser";
    public string _password = "1passwordHere!";

	void Start () {

        _mumbleClient = new MumbleClient(hostName, port, DebuggingVariables);
        _mumbleClient.Connect(_username, _password);

        MyMumbleAudioPlayer.Initialize(_mumbleClient);
        MyMumbleAudioSender.Initialize(_mumbleClient);

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
