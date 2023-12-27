//Original from http://wiki.unity3d.com/index.php/EditorGraphWindow

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;

public class GraphChannel
{
    public int numPoints = 0;
    public float[] _data = new float[Graph.MAX_HISTORY];
    public Color _color = Color.white;
    public bool isActive = false;

    private float yMin, yMax;

    public GraphChannel(Color _C)
    {
        _color = _C;
    }

    public void Feed(float val)
    {
        if(val > yMax)
        {
            yMax = val;
            Graph.UpdateMax(yMin, yMax);
        }
        if(val < yMin)
        {
            yMin = val;
            Graph.UpdateMax(yMin, yMax);
        }
        //Shift all values over
        for (int i = Graph.MAX_HISTORY - 1; i >= 1; i--)
            _data[i] = _data[i - 1];

        _data[0] = val;

        numPoints = Mathf.Min(numPoints + 1, Graph.MAX_HISTORY);
        isActive = true;
    }
}

public class Graph
{
    public static float YMin, YMax;

    public const int MAX_HISTORY = 1024;
    public const int MAX_CHANNELS = 3;

    public static GraphChannel[] channel = new GraphChannel[MAX_CHANNELS];

    static Graph()
    {
        Graph.channel[0] = new GraphChannel(Color.gray);
        Graph.channel[1] = new GraphChannel(Color.blue);
        Graph.channel[2] = new GraphChannel(Color.red);
    }
    public static void UpdateMax(float newMin, float newMax)
    {
        //Debug.Log(newMin + " " + newMax);
        YMin = Mathf.Min(YMin, newMin);
        YMax = Mathf.Max(YMax, newMax);
        //Debug.Log("updating max to: " + YMin + ", " + YMax);
    }
}

#if UNITY_EDITOR
public class EditorGraph : EditorWindow
{

    [MenuItem("Window/Graph")]
    static void ShowGraph()
    {
        EditorWindow.GetWindow<EditorGraph>();
    }

    Material lineMaterial;

    void OnEnable()
    {
        EditorApplication.update += MyDelegate;
    }

    void OnDisable()
    {
        EditorApplication.update -= MyDelegate;
    }

    void MyDelegate()
    {
        Repaint();
    }

    void CreateLineMaterial()
    {
        if (!lineMaterial)
        {
            lineMaterial = new Material(Shader.Find("Unlit/GraphShader"));
            lineMaterial.hideFlags = HideFlags.HideAndDontSave;
            lineMaterial.shader.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
            return;

        if (Graph.channel[0] == null)
            return;

        int W = (int)this.position.width;
        int H = (int)this.position.height;

        CreateLineMaterial();
        lineMaterial.SetPass(0);

        GL.PushMatrix();
        GL.LoadPixelMatrix();

        GL.Begin(GL.LINES);

        for (int chan = 0; chan < Graph.MAX_CHANNELS; chan++)
        {
            GraphChannel C = Graph.channel[chan];

            if (!C.isActive)
                continue;

            GL.Color(C._color);

            for (int h = 0; h < Graph.MAX_HISTORY; h++)
            {
                int xPix = (W - 1) - h;

                if (xPix >= 0)
                {
                    float y = C._data[h];

                    float y_01 = Mathf.InverseLerp(Graph.YMin, Graph.YMax, y);

                    int yPix = (int)(y_01 * H);

                    Plot(xPix, yPix);
                }
            }
        }

        GL.End();

        GL.PopMatrix();
    }

    // plot an X
    void Plot(float x, float y)
    {
        // first line of X
        GL.Vertex3(x - 1, y - 1, 0);
        GL.Vertex3(x + 1, y + 1, 0);

        // second
        GL.Vertex3(x - 1, y + 1, 0);
        GL.Vertex3(x + 1, y - 1, 0);
    }
}
#endif