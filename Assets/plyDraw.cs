using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;
using System;
using UnityEditor;
using System.Threading;
using System.Net.Sockets;
using System.Text;
using System.Collections;

public class plyDraw : MonoBehaviour {

    // debug control
    private Boolean DEBUG_FLAG = false;

    // tcp connection
    private const string SERVER_IP = "192.168.1.128";
    private const Int32 SERVER_PORT = 8000;
    private TcpClient socketConnection;
    private Thread socketTread;
    private NetworkStream stream;

    // camera data buffer
    private Queue<byte[]> pclBuffer = new Queue<byte[]> ();

    // buffer access queue
    private object bufferLock = new object();

    // camera server control message
    private const String SERVER_CTRL_MSG = "YZ";
    private const byte COLORLESS_PCL = 0;
    private const byte COLOR_PCL = 1;

    // Unity objects
    private MeshFilter meshFilter;
    private MeshRenderer meshRender;

    // PCL location
    float x_pcl = 0, y_pcl = 0, z_pcl = 0;

    // PCL package object
    private PointCloudPackage pointCloud = new PointCloudPackage();

    // Helmet view camera (for demo)
    public Camera cam;

    // PCL package
    class PointCloudPackage
    {
        public List<Vector3> vertices;
        public List<Color32> colors;
        public int vertexCount;
        public int size;

        public PointCloudPackage() {
            vertexCount = 0;
            size = 0;
        }

        public void InitPointSpace() {
            vertices = new List<Vector3>(vertexCount);
            colors = new List<Color32>(vertexCount);
        }

        public void AddPoint(float x, float y, float z, byte r, byte g, byte b, byte a) {
            vertices.Add(new Vector3(x, y, z));
            colors.Add(new Color32(r, g, b, a));
        }
    }

    // Use this for initialization
    void Start()
    {
        // create new thread to build the connection
        socketTread = new Thread(new ThreadStart(socketThreadLoop));
        socketTread.Start();

        // create a new MeshFilter
        meshFilter = gameObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = new Mesh();
        meshFilter.sharedMesh.MarkDynamic();
        meshFilter.sharedMesh.name = "arena";

        // create a new MeshRender
        meshRender = gameObject.AddComponent<MeshRenderer>();
        meshRender.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Pcx/Editor/Default Point.mat");

        // calibrate orientation
        Quaternion rot = Quaternion.Euler(0, 0, 180);
        gameObject.transform.rotation = rot;

        // move PCL in front of camera, demo only
        float x_cam = 65;
        float y_cam = 65;
        float z_cam = -7;
        meshFilter.transform.position = new Vector3(x_cam, y_cam, z_cam);
    }

    // Update is called once per frame
    void Update()
    {
        byte[] pclData = null;

        lock (bufferLock) {
            if (pclBuffer.Count != 0)
                pclData = pclBuffer.Dequeue();
        }

        if ((pclData != null) && (pclData.Length != 0)) {
            pointCloud.vertexCount = pclData.Length / 10;
            pointCloud.InitPointSpace();
            ReadPointData(pclData);

            // transfer point cloud data to mesh
            Mesh mesh = meshFilter.sharedMesh;
            mesh.Clear();
            mesh.indexFormat = pointCloud.vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(pointCloud.vertices);
            mesh.SetColors(pointCloud.colors);
            mesh.SetIndices(Enumerable.Range(0, pointCloud.vertexCount).ToArray(), MeshTopology.Points, 0);
            mesh.UploadMeshData(false);
        }
    }

    private void OnDisable()
    {
        socketConnection.Close();
    }

    private void socketThreadLoop()
    {
        // create buffer
        pclBuffer = new Queue<byte[]> ();

        // build connection
        socketConnection = new TcpClient(SERVER_IP, SERVER_PORT);
        stream = socketConnection.GetStream();

        while (true) {
            // get new point cloud data
            byte[] pointData = ListenNewData();

            lock(bufferLock) {
                pclBuffer.Enqueue(pointData);
            }
        }
    }

    private byte[] ListenNewData()
    {
        Int32 readByte = 0;
        byte[] pointDataBuffer;

        // request color pointcloud (XYZRGB)
        stream.Write(Encoding.ASCII.GetBytes(SERVER_CTRL_MSG), COLOR_PCL, 1);

        // get data size & data
        byte[] bufferSize = new byte[sizeof(int)];
        while (readByte < sizeof(int))
            readByte += stream.Read(bufferSize, 0, sizeof(int));

        pointCloud.size = BitConverter.ToInt32(bufferSize, 0);
        pointDataBuffer = new byte[pointCloud.size];

        readByte = 0;
        while (readByte < pointCloud.size)
        {
            var readSize = stream.Read(pointDataBuffer, readByte, pointDataBuffer.Length - readByte);
            readByte += readSize;
        }

        return pointDataBuffer;
    }

    private void ReadPointData(byte[] dataBuffer)
    {
        const float CONV_RATE = 1000.0f;
        float x = 0, y = 0, z = 0;
        Byte r = 255, g = 255, b = 255, a = 255;

        if (pointCloud.vertexCount != 0)
        {
            for (var i = 0; i < pointCloud.size; i += 10)
            {
                x = (dataBuffer[i] | (dataBuffer[i + 1] << 8)) / CONV_RATE;
                y = (dataBuffer[i + 2] | (dataBuffer[i + 3] << 8)) / CONV_RATE;
                z = (dataBuffer[i + 4] | (dataBuffer[i + 5] << 8)) / CONV_RATE;
                r = dataBuffer[i + 6];
                g = dataBuffer[i + 7];
                b = dataBuffer[i + 8];

                pointCloud.AddPoint(x, y, z, r, g, b, a);

                if (DEBUG_FLAG) {
                    x_pcl += x; y_pcl += y; z_pcl += z;
                }
            }

            if (DEBUG_FLAG) {
                x_pcl /= pointCloud.vertexCount;
                y_pcl /= pointCloud.vertexCount;
                z_pcl /= pointCloud.vertexCount;

                Debug.Log("===== Coordinate Average =====");
                Debug.Log("X: " + x_pcl);
                Debug.Log("Y: " + y_pcl);
                Debug.Log("Z: " + z_pcl);
            }
        }
    }
}
