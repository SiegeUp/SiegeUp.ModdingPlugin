using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SiegeUp.ModdingPlugin.Editor
{
	[CustomEditor(typeof(ModCommandsExecutor))]
	public class TestingToolGUI : UnityEditor.Editor
	{
        ModCommandsExecutor _targetObject;
        string _command = "";

        void OnEnable() => _targetObject = (ModCommandsExecutor)target;

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();
			_command = GUILayout.TextField(_command);
			if (GUILayout.Button("Execute"))
			{
				_targetObject.Execute(_command.Split().ToList());
			}
		}
	}
}
