using AIRTS.Lockstep.Server;
using UnityEngine;

namespace AIRTS.Lockstep.Unity
{
    public sealed class LockstepServerBehaviour : MonoBehaviour
    {
        [SerializeField] private int port = 7777;
        [SerializeField] private int frameRate = 15;
        [SerializeField] private int inputDelay = 2;
        [SerializeField] private bool startOnAwake = true;
        [SerializeField] private bool logFrames = false;

        private LockstepServer _server;

        private void Awake()
        {
            if (startOnAwake)
            {
                StartServer();
            }
        }

        public void StartServer()
        {
            if (_server != null)
            {
                return;
            }

            _server = new LockstepServer(frameRate, inputDelay);
            _server.Log += Debug.Log;
            _server.ClientConnected += playerId => Debug.Log("Lockstep client joined: " + playerId);
            _server.ClientDisconnected += playerId => Debug.Log("Lockstep client left: " + playerId);
            _server.FrameAdvanced += (frame, commandCount) =>
            {
                if (logFrames)
                {
                    Debug.Log("Server frame " + frame + ", commands: " + commandCount);
                }
            };
            _server.Start(port);
        }

        public void StopServer()
        {
            _server?.Dispose();
            _server = null;
        }

        private void OnDestroy()
        {
            StopServer();
        }
    }
}
