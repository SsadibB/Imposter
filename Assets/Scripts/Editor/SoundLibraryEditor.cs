using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SoundLibrary))]
public class SoundLibraryEditor : Editor
{
    private SerializedProperty _masterVolume;
    private SerializedProperty _musicVolume;
    private SerializedProperty _sfxVolume;
    private SerializedProperty _musicList;
    private SerializedProperty _sfxList;

    private bool _showMusicSection = true;
    private bool _showSfxSection = true;

    void OnEnable()
    {
        _masterVolume = serializedObject.FindProperty("masterVolume");
        _musicVolume = serializedObject.FindProperty("musicVolume");
        _sfxVolume = serializedObject.FindProperty("sfxVolume");
        _musicList = serializedObject.FindProperty("music");
        _sfxList = serializedObject.FindProperty("sfx");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SoundLibrary lib = (SoundLibrary)target;

        // Title Header
        EditorGUILayout.Space(8);
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter
        };
        EditorGUILayout.LabelField("🎵 SOUND LIBRARY", titleStyle);
        EditorGUILayout.HelpBox("Configure unlimited Music tracks and SFX with individual Loop settings.\n- Loop ON: Audio repeats indefinitely.\n- Loop OFF: Audio plays once.", MessageType.Info);
        EditorGUILayout.Space(6);

        // Volume Sliders
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Global Volume Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_masterVolume, new GUIContent("Master Volume"));
        EditorGUILayout.PropertyField(_musicVolume, new GUIContent("Music Volume"));
        EditorGUILayout.PropertyField(_sfxVolume, new GUIContent("SFX Volume"));
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);

        // 1. MUSIC SECTION
        DrawAudioSection("🎼 MUSIC LIBRARY", _musicList, ref _showMusicSection, new Color(0.2f, 0.6f, 0.9f, 0.25f), true);

        EditorGUILayout.Space(8);

        // 2. SFX SECTION
        DrawAudioSection("🔊 SOUND EFFECTS (SFX)", _sfxList, ref _showSfxSection, new Color(0.9f, 0.5f, 0.2f, 0.25f), false);

        EditorGUILayout.Space(12);

        // Runtime controls when playing
        if (Application.isPlaying)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Runtime Debug Playback", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Stop Music"))
            {
                lib.StopMusic();
            }
            if (GUILayout.Button("Stop All SFX"))
            {
                lib.StopAllSFX();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawAudioSection(string title, SerializedProperty listProp, ref bool foldout, Color headerBg, bool isMusic)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        GUIStyle headerStyle = new GUIStyle(EditorStyles.foldoutHeader)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold
        };

        EditorGUILayout.BeginHorizontal();
        foldout = EditorGUILayout.Foldout(foldout, $"{title} ({listProp.arraySize} Entries)", true, headerStyle);
        if (GUILayout.Button("+ Add Entry", EditorStyles.miniButtonRight, GUILayout.Width(90)))
        {
            listProp.InsertArrayElementAtIndex(listProp.arraySize);
            SerializedProperty newElem = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
            newElem.FindPropertyRelative("id").stringValue = isMusic ? $"Music_{listProp.arraySize}" : $"SFX_{listProp.arraySize}";
            newElem.FindPropertyRelative("clip").objectReferenceValue = null;
            newElem.FindPropertyRelative("volume").floatValue = 1f;
            newElem.FindPropertyRelative("loop").boolValue = isMusic; // Music defaults to loop, SFX defaults to false
            newElem.FindPropertyRelative("pitch").floatValue = 1f;
            foldout = true;
        }
        EditorGUILayout.EndHorizontal();

        if (foldout)
        {
            EditorGUILayout.Space(4);
            if (listProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("No entries yet. Click '+ Add Entry' above to add unlimited tracks.", MessageType.None);
            }

            for (int i = 0; i < listProp.arraySize; i++)
            {
                SerializedProperty elem = listProp.GetArrayElementAtIndex(i);
                SerializedProperty pId = elem.FindPropertyRelative("id");
                SerializedProperty pClip = elem.FindPropertyRelative("clip");
                SerializedProperty pVol = elem.FindPropertyRelative("volume");
                SerializedProperty pLoop = elem.FindPropertyRelative("loop");
                SerializedProperty pPitch = elem.FindPropertyRelative("pitch");

                EditorGUILayout.BeginVertical(EditorStyles.textArea);

                // Entry Header: Index, ID, and Delete Button
                EditorGUILayout.BeginHorizontal();
                string displayId = string.IsNullOrEmpty(pId.stringValue) ? $"Entry #{i}" : pId.stringValue;
                string loopTag = pLoop.boolValue ? "[🔁 LOOP]" : "[PLAY ONCE]";
                EditorGUILayout.LabelField($"#{i + 1}: {displayId}  {loopTag}", EditorStyles.boldLabel);

                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    listProp.DeleteArrayElementAtIndex(i);
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                // Entry Fields
                EditorGUILayout.PropertyField(pId, new GUIContent("Identifier (ID)"));
                EditorGUILayout.PropertyField(pClip, new GUIContent("Audio Clip"));
                EditorGUILayout.PropertyField(pVol, new GUIContent("Volume"));

                // LOOP CHECKBOX (Highlighted with distinct style)
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(pLoop, new GUIContent("Loop Audio", "If checked, automatically repeats indefinitely. If unchecked, plays only once."));
                if (pLoop.boolValue)
                {
                    GUIStyle loopBadge = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = new Color(0.2f, 0.8f, 0.3f) },
                        fontStyle = FontStyle.Bold
                    };
                    EditorGUILayout.LabelField("🔁 Will repeat indefinitely", loopBadge);
                }
                else
                {
                    GUIStyle onceBadge = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = Color.gray }
                    };
                    EditorGUILayout.LabelField("▶ Plays once only", onceBadge);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(pPitch, new GUIContent("Pitch"));

                // Play / Preview button in Play Mode
                if (Application.isPlaying && pClip.objectReferenceValue != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(pLoop.boolValue ? "▶ Play (Looping)" : "▶ Play (Once)", EditorStyles.miniButtonLeft))
                    {
                        SoundLibrary lib = (SoundLibrary)target;
                        if (isMusic) lib.PlayMusic(pId.stringValue);
                        else lib.PlaySFX(pId.stringValue);
                    }
                    if (GUILayout.Button("⏹ Stop", EditorStyles.miniButtonRight, GUILayout.Width(60)))
                    {
                        SoundLibrary lib = (SoundLibrary)target;
                        if (isMusic) lib.StopMusic();
                        else lib.StopSFX(pId.stringValue);
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
        }

        EditorGUILayout.EndVertical();
    }
}
