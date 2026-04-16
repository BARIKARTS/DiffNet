using UnityEngine;
using DifferentGames.Multiplayer.Core;
using DifferentGames.Multiplayer.Integration;

namespace DifferentGames.Multiplayer.Samples
{
    /// <summary>
    /// Concrete DiffNet implementation ready to be placed inside a Unity Scene.
    /// </summary>
    public class DiffNetStarter : DiffNetManagerBase
    {
        private void OnGUI()
        {
            if (Runner == null || Runner.IsRunning) return;

            GUILayout.BeginArea(new Rect(10, 10, 200, 300));
            
            if (GUILayout.Button("Start Server", GUILayout.Height(40))) StartServer();
            if (GUILayout.Button("Start Client", GUILayout.Height(40))) StartClient();

            GUILayout.EndArea();
        }

        public override void OnProvideInput(NetworkRunner runner, NetworkInputProvider input)
        {
            // Gather Unity inputs and inject them directly into DiffNet Prediction engine
            var myInput = new BasicPlayerInput
            {
                Movement = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")),
                IsJumping = Input.GetKeyDown(KeyCode.Space)
            };
            
            input.Set(myInput);
        }
    }
}
