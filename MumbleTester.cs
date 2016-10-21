using UnityEngine;
using System.Collections;
using Mumble;

public class MumbleTester : MonoBehaviour {

    public MumbleAudioPlayer[] MumbleAudioPlayers;
    public SendMumbleAudio AudioSender;

    private MumbleClient _mumbleClient;
    public string hostName = "1.2.3.4";
    public int port = 64738;
    public string _username = "SuperUser";
    public string _password = "1passwordHere!";

	void Start () {

        _mumbleClient = new MumbleClient(hostName, port);
        _mumbleClient.Connect(_username, _password);
        
        foreach(MumbleAudioPlayer audioPlayer in MumbleAudioPlayers)
        {
            audioPlayer.SetMumbleClient(_mumbleClient);
        }
        AudioSender.Initialize(_mumbleClient);
	}
	
    void OnApplicationQuit()
    {
        Debug.LogWarning("Shutting down connections");
        if(_mumbleClient != null)
            _mumbleClient.Close();
    }
	void Update () {

        if (Input.GetKeyDown(KeyCode.P))
        {
            //_mumbleClient.Process();
        }
        if (Input.GetKeyDown(KeyCode.I))
        {
            print("Is Connected: " + _mumbleClient.ConnectionSetupFinished);
        }
        if (Input.GetKeyDown(KeyCode.S))
        {
            _mumbleClient.SendTextMessage("This is an example message from Unity");
            print("Sent mumble message");
        }
	}
}
