// Original from http://wiki.unity3d.com/index.php/EditorGraphWindow

using UnityEditor;
using UnityEngine;

namespace Mumble.Editor
{
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
            if (val > yMax)
            {
                yMax = val;
                Graph.UpdateMax(yMin, yMax);
            }
            if (val < yMin)
            {
                yMin = val;
                Graph.UpdateMax(yMin, yMax);
            }

            // Shift all values over
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
            channel[0] = new GraphChannel(Color.gray);
            channel[1] = new GraphChannel(Color.blue);
            channel[2] = new GraphChannel(Color.red);
        }

        public static void UpdateMax(float newMin, float newMax)
        {
            YMin = Mathf.Min(YMin, newMin);
            YMax = Mathf.Max(YMax, newMax);
        }
    }

    public class EditorGraph : EditorWindow
    {
        [MenuItem("Window/Graph")]
        static void ShowGraph()
        {
            GetWindow<EditorGraph>();
        }

        Material lineMaterial;

        void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnEditorUpdate()
        {
            Repaint();
        }

        void CreateLineMaterial()
        {
            if (!lineMaterial)
            {
                lineMaterial = new Material(Shader.Find("Unlit/GraphShader"))
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                lineMaterial.shader.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (Graph.channel[0] == null)
                return;

            int W = (int)position.width;
            int H = (int)position.height;

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

        // Plot an X
        void Plot(float x, float y)
        {
            // First line of X
            GL.Vertex3(x - 1, y - 1, 0);
            GL.Vertex3(x + 1, y + 1, 0);

            // Second
            GL.Vertex3(x - 1, y + 1, 0);
            GL.Vertex3(x + 1, y - 1, 0);
        }
    }
}