using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace nfya.MoleTool
{
    public class MoleTool : EditorWindow
    {
        private SkinnedMeshRenderer targetRenderer;

private Mesh bakedMesh;
        private Vector3[] worldVertices;
        private Vector3[] worldNormals;
        private int[] triangles;
        private Vector2[] uvs;

private float moleRadius = 0.0005f;
        private Color moleColor = new Color(0.2f, 0.12f, 0.08f, 1f);
        private float moleSoftness = 0.5f;
        private Texture2D stampTexture;

private bool isPlacing;
        private Vector3 previewWorldPos;
        private Vector3 previewNormal;
        private Vector2 previewUV;
        private float previewUVRadius;
        private bool hasPreviewHit;

[System.Serializable]
        private class MolePlacement
        {
            public Vector2 uv;
            public float uvRadius;
            public Color color;
            public float softness;
            public float worldRadius;
        }
        private List<MolePlacement> placedMoles = new List<MolePlacement>();

private Texture2D readableSource;
        private Texture2D previewTexture;
        private Material targetMaterial;
        private Texture originalMainTex;
        private string texturePropertyName;

private Vector2 listScroll;
        private bool showAdvanced;

private const float SIZE_MIN = 0.0001f;
        private const float SIZE_MAX = 0.002f;
        private const float SCROLL_STEP = 0.00005f;

        [MenuItem("nfya/Mole Tool")]
        public static void ShowWindow()
        {
            var win = GetWindow<MoleTool>("Mole Tool");
            win.minSize = new Vector2(280, 300);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            SafeRestoreTexture();
        }

        private void OnDestroy()
        {
            SafeRestoreTexture();
        }

private void OnGUI()
        {

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            targetRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                "Target", targetRenderer, typeof(SkinnedMeshRenderer), true);
            if (EditorGUI.EndChangeCheck())
            {
                OnTargetChanged();
            }

            if (targetRenderer == null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(
                    "SkinnedMeshRenderer を上のフィールドにセットするか、\n" +
                    "Hierarchy で対象オブジェクトを選択してください。",
                    MessageType.Info);
                return;
            }

if (targetMaterial == null || originalMainTex == null)
            {
                PrepareTexture();
                if (targetMaterial == null || originalMainTex == null)
                {
                    EditorGUILayout.HelpBox(
                        "対象のマテリアルにテクスチャが見つかりません。\n" +
                        "メインテクスチャが設定されているか確認してください。",
                        MessageType.Warning);
                    return;
                }
            }

EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("ほくろ設定", EditorStyles.boldLabel);

EditorGUILayout.BeginHorizontal();
            moleRadius = EditorGUILayout.Slider("サイズ", moleRadius, SIZE_MIN, SIZE_MAX);
            EditorGUILayout.EndHorizontal();

            moleColor = EditorGUILayout.ColorField("色", moleColor);

showAdvanced = EditorGUILayout.Foldout(showAdvanced, "詳細設定");
            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                moleSoftness = EditorGUILayout.Slider("ぼかし", moleSoftness, 0f, 1f);
                stampTexture = (Texture2D)EditorGUILayout.ObjectField(
                    "スタンプ画像", stampTexture, typeof(Texture2D), false);
                if (stampTexture != null)
                {
                    EditorGUILayout.HelpBox(
                        "スタンプ画像の暗い部分がほくろの形状になります。\n" +
                        "透明度(Alpha)も反映されます。", MessageType.None);
                }
                EditorGUI.indentLevel--;
            }

EditorGUILayout.Space(8);
            Color prevBg = GUI.backgroundColor;
            if (isPlacing) GUI.backgroundColor = new Color(0.3f, 0.9f, 0.3f);
            string btnLabel = isPlacing ? "■  配置モード ON" : "▶  配置モード開始";
            if (GUILayout.Button(btnLabel, GUILayout.Height(32)))
            {
                TogglePlacingMode();
            }
            GUI.backgroundColor = prevBg;

            if (isPlacing)
            {
                var helpStyle = new GUIStyle(EditorStyles.helpBox) { richText = true };
                EditorGUILayout.TextArea(
                    "<b>左クリック</b>  ほくろを配置\n" +
                    "<b>スクロール</b>  サイズ調整\n" +
                    "<b>Escape</b>       配置モード終了",
                    helpStyle);
            }

if (placedMoles.Count > 0)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField($"配置済み ({placedMoles.Count})", EditorStyles.boldLabel);

                listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.MaxHeight(150));
                for (int i = placedMoles.Count - 1; i >= 0; i--)
                {
                    var mole = placedMoles[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"  #{i + 1}", GUILayout.Width(32));
                    mole.color = EditorGUILayout.ColorField(GUIContent.none, mole.color,
                        false, false, false, GUILayout.Width(36));
                    EditorGUILayout.LabelField(FormatSize(mole.worldRadius), GUILayout.Width(60));
                    if (GUILayout.Button("×", GUILayout.Width(22)))
                    {
                        placedMoles.RemoveAt(i);
                        UpdatePreviewTexture();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(4);

GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
                if (GUILayout.Button("テクスチャに焼き込み保存", GUILayout.Height(30)))
                {
                    BakeAndSave();
                }
                GUI.backgroundColor = prevBg;

                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("全てクリア", GUILayout.Width(80)))
                {
                    if (placedMoles.Count <= 1 ||
                        EditorUtility.DisplayDialog("確認",
                            $"{placedMoles.Count}個のほくろを全て削除しますか？", "削除", "キャンセル"))
                    {
                        placedMoles.Clear();
                        UpdatePreviewTexture();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private string FormatSize(float radius)
        {

            float diameter = radius * 2f * 1000f;
            return $"{diameter:F2}mm";
        }

private void OnSelectionChanged()
        {
            if (isPlacing) return;

            var go = Selection.activeGameObject;
            if (go == null) return;

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr != targetRenderer)
            {
                SafeRestoreTexture();
                targetRenderer = smr;
                OnTargetChanged();
                Repaint();
            }
        }

        private void OnTargetChanged()
        {
            SafeRestoreTexture();
            placedMoles.Clear();
            isPlacing = false;
            readableSource = null;
            previewTexture = null;
            targetMaterial = null;
            originalMainTex = null;
            texturePropertyName = null;

            if (targetRenderer != null)
            {
                PrepareTexture();
            }
        }

        private void TogglePlacingMode()
        {
            isPlacing = !isPlacing;
            if (isPlacing)
            {
                BakeMeshData();
                PrepareTexture();

                if (worldVertices == null || readableSource == null)
                {
                    EditorUtility.DisplayDialog("エラー",
                        "メッシュまたはテクスチャの準備に失敗しました。\n" +
                        "Target が正しく設定されているか確認してください。", "OK");
                    isPlacing = false;
                    return;
                }

if (SceneView.lastActiveSceneView != null)
                {
                    SceneView.lastActiveSceneView.Focus();
                }
            }
            else
            {
                hasPreviewHit = false;
            }
        }

        private void OnUndoRedo()
        {
            UpdatePreviewTexture();
            Repaint();
        }

private void BakeMeshData()
        {
            if (targetRenderer == null) return;

            if (bakedMesh == null) bakedMesh = new Mesh();
            targetRenderer.BakeMesh(bakedMesh);

            var localVerts = bakedMesh.vertices;
            var localNorms = bakedMesh.normals;
            triangles = bakedMesh.triangles;
            uvs = bakedMesh.uv;

            if (localVerts == null || localVerts.Length == 0 ||
                triangles == null || triangles.Length == 0 ||
                uvs == null || uvs.Length == 0)
            {
                worldVertices = null;
                return;
            }

            var tf = targetRenderer.transform;
            worldVertices = new Vector3[localVerts.Length];
            worldNormals = new Vector3[localNorms.Length];
            for (int i = 0; i < localVerts.Length; i++)
            {
                worldVertices[i] = tf.TransformPoint(localVerts[i]);
                if (i < localNorms.Length)
                    worldNormals[i] = tf.TransformDirection(localNorms[i]).normalized;
            }
        }

        private void PrepareTexture()
        {
            if (targetRenderer == null) return;

            var mat = targetRenderer.sharedMaterial;
            if (mat == null) return;

texturePropertyName = FindMainTextureProperty(mat);
            if (texturePropertyName == null) return;

            var tex = mat.GetTexture(texturePropertyName) as Texture2D;
            if (tex == null) return;

            targetMaterial = mat;
            originalMainTex = tex;
            readableSource = MakeReadable(tex);
        }

        private static string FindMainTextureProperty(Material mat)
        {

            string[] candidates = {
                "_MainTex",
                "_BaseMap",
                "_BaseColorMap",
            };

            foreach (var prop in candidates)
            {
                if (mat.HasProperty(prop) && mat.GetTexture(prop) != null)
                    return prop;
            }

if (mat.mainTexture != null)
                return "_MainTex";

            return null;
        }

private void OnSceneGUI(SceneView sceneView)
        {
            if (!isPlacing || targetRenderer == null || worldVertices == null) return;

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                isPlacing = false;
                hasPreviewHit = false;
                e.Use();
                Repaint();
                return;
            }

if (e.type == EventType.ScrollWheel)
            {
                moleRadius = Mathf.Clamp(
                    moleRadius + (e.delta.y > 0 ? -SCROLL_STEP : SCROLL_STEP),
                    SIZE_MIN, SIZE_MAX);
                e.Use();
                Repaint();
                sceneView.Repaint();
                return;
            }

if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag ||
                e.type == EventType.MouseDown || e.type == EventType.Layout)
            {
                if (e.type == EventType.Layout)
                {
                    HandleUtility.AddDefaultControl(controlId);
                    return;
                }

                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                hasPreviewHit = RaycastMesh(ray, out previewWorldPos, out previewNormal,
                    out previewUV, out previewUVRadius);

                if (e.type == EventType.MouseDown && e.button == 0 && hasPreviewHit)
                {
                    PlaceMole();
                    e.Use();
                }
            }

if (hasPreviewHit)
            {
                DrawMoleGizmo(previewWorldPos, previewNormal, moleRadius, moleColor, true);
            }

Handles.BeginGUI();
            var overlayRect = new Rect(10, sceneView.position.height - 90, 220, 60);
            GUI.Box(overlayRect, GUIContent.none, EditorStyles.helpBox);
            overlayRect.x += 8;
            overlayRect.y += 6;
            var labelStyle = new GUIStyle(EditorStyles.miniLabel) { richText = true };
            GUI.Label(overlayRect,
                $"<b>Mole Tool</b>  サイズ: {FormatSize(moleRadius)}\n" +
                "左クリック: 配置 / スクロール: サイズ / Esc: 終了",
                labelStyle);
            Handles.EndGUI();

            sceneView.Repaint();
        }

        private void DrawMoleGizmo(Vector3 pos, Vector3 normal, float radius, Color color, bool showOuter)
        {

            Handles.color = new Color(color.r, color.g, color.b, 0.6f);
            Handles.DrawSolidDisc(pos, normal, radius);

Handles.color = Color.white;
            Handles.DrawWireDisc(pos, normal, radius);

if (showOuter && moleSoftness > 0.01f)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.25f);
                Handles.DrawWireDisc(pos, normal, radius * (1f + moleSoftness * 0.5f));
            }
        }

private bool RaycastMesh(Ray ray, out Vector3 hitPos, out Vector3 hitNormal,
            out Vector2 hitUV, out float hitUVRadius)
        {
            hitPos = Vector3.zero;
            hitNormal = Vector3.up;
            hitUV = Vector2.zero;
            hitUVRadius = 0f;

            float closestDist = float.MaxValue;
            bool hit = false;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                Vector3 v0 = worldVertices[i0];
                Vector3 v1 = worldVertices[i1];
                Vector3 v2 = worldVertices[i2];

                if (RayTriangleIntersect(ray, v0, v1, v2, out float dist, out float u, out float v))
                {
                    if (dist > 0 && dist < closestDist)
                    {
                        closestDist = dist;
                        float w = 1f - u - v;
                        hitPos = w * v0 + u * v1 + v * v2;
                        hitNormal = (w * worldNormals[i0] + u * worldNormals[i1] + v * worldNormals[i2]).normalized;
                        hitUV = w * uvs[i0] + u * uvs[i1] + v * uvs[i2];
                        hitUVRadius = EstimateUVRadius(v0, v1, v2, uvs[i0], uvs[i1], uvs[i2], moleRadius);
                        hit = true;
                    }
                }
            }

            return hit;
        }

        private static bool RayTriangleIntersect(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2,
            out float t, out float u, out float v)
        {
            t = 0; u = 0; v = 0;
            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.direction, e2);
            float a = Vector3.Dot(e1, h);
            if (a > -1e-6f && a < 1e-6f) return false;

            float f = 1f / a;
            Vector3 s = ray.origin - v0;
            u = f * Vector3.Dot(s, h);
            if (u < 0f || u > 1f) return false;

            Vector3 q = Vector3.Cross(s, e1);
            v = f * Vector3.Dot(ray.direction, q);
            if (v < 0f || u + v > 1f) return false;

            t = f * Vector3.Dot(e2, q);
            return t > 1e-6f;
        }

        private static float EstimateUVRadius(Vector3 v0, Vector3 v1, Vector3 v2,
            Vector2 uv0, Vector2 uv1, Vector2 uv2, float worldRadius)
        {
            float worldArea = Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;
            float uvArea = Mathf.Abs((uv1.x - uv0.x) * (uv2.y - uv0.y) -
                                     (uv2.x - uv0.x) * (uv1.y - uv0.y)) * 0.5f;

            if (worldArea < 1e-10f) return 0.01f;
            float ratio = Mathf.Sqrt(uvArea / worldArea);
            return worldRadius * ratio;
        }

private void PlaceMole()
        {
            var mole = new MolePlacement
            {
                uv = previewUV,
                uvRadius = previewUVRadius,
                color = moleColor,
                softness = moleSoftness,
                worldRadius = moleRadius
            };
            placedMoles.Add(mole);
            UpdatePreviewTexture();
            Repaint();
        }

        private void UpdatePreviewTexture()
        {
            if (readableSource == null || targetMaterial == null) return;

            int w = readableSource.width;
            int h = readableSource.height;

            if (previewTexture == null || previewTexture.width != w || previewTexture.height != h)
            {
                previewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            }
            previewTexture.SetPixels(readableSource.GetPixels());

            foreach (var mole in placedMoles)
            {
                PaintMole(previewTexture, mole);
            }

            previewTexture.Apply();
            targetMaterial.SetTexture(texturePropertyName, previewTexture);
        }

        private void PaintMole(Texture2D tex, MolePlacement mole)
        {
            int w = tex.width;
            int h = tex.height;

            float uvR = mole.uvRadius;
            if (uvR < 1e-6f) return;

            float outerUVR = uvR * (1f + mole.softness * 0.5f);

Texture2D readableStamp = null;
            if (stampTexture != null)
            {
                readableStamp = MakeReadable(stampTexture);
            }

            int minPx = Mathf.Max(0, Mathf.FloorToInt((mole.uv.x - outerUVR) * w) - 1);
            int maxPx = Mathf.Min(w - 1, Mathf.CeilToInt((mole.uv.x + outerUVR) * w) + 1);
            int minPy = Mathf.Max(0, Mathf.FloorToInt((mole.uv.y - outerUVR) * h) - 1);
            int maxPy = Mathf.Min(h - 1, Mathf.CeilToInt((mole.uv.y + outerUVR) * h) + 1);

            for (int py = minPy; py <= maxPy; py++)
            {
                for (int px = minPx; px <= maxPx; px++)
                {
                    float u = (px + 0.5f) / w;
                    float v = (py + 0.5f) / h;

                    float du = u - mole.uv.x;
                    float dv = v - mole.uv.y;
                    float dist = Mathf.Sqrt(du * du + dv * dv);

                    if (dist > outerUVR) continue;

                    float alpha;
                    if (readableStamp != null)
                    {
                        float su = Mathf.Clamp01((du / outerUVR + 1f) * 0.5f);
                        float sv = Mathf.Clamp01((dv / outerUVR + 1f) * 0.5f);
                        Color stampPixel = readableStamp.GetPixelBilinear(su, sv);
                        alpha = stampPixel.a * (1f - stampPixel.grayscale);
                    }
                    else
                    {
                        if (dist <= uvR)
                            alpha = 1f;
                        else
                            alpha = 1f - (dist - uvR) / (outerUVR - uvR);
                    }

                    alpha = Mathf.Clamp01(alpha);
                    if (alpha < 0.001f) continue;

                    Color orig = tex.GetPixel(px, py);
                    Color blended = Color.Lerp(orig, mole.color, alpha);
                    blended.a = orig.a;
                    tex.SetPixel(px, py, blended);
                }
            }
        }

private void BakeAndSave()
        {
            if (previewTexture == null || targetMaterial == null)
            {
                EditorUtility.DisplayDialog("エラー", "プレビューテクスチャがありません。", "OK");
                return;
            }

            string origPath = originalMainTex != null ? AssetDatabase.GetAssetPath(originalMainTex) : "";
            string dir = string.IsNullOrEmpty(origPath) ? "Assets" : Path.GetDirectoryName(origPath);
            string baseName = string.IsNullOrEmpty(origPath)
                ? targetRenderer.name
                : Path.GetFileNameWithoutExtension(origPath);

            string savePath = EditorUtility.SaveFilePanel(
                "ほくろテクスチャを保存", dir, baseName + "_mole", "png");

            if (string.IsNullOrEmpty(savePath)) return;

            string dataPath = Application.dataPath.Replace('\\', '/');
            savePath = savePath.Replace('\\', '/');
            if (!savePath.StartsWith(dataPath))
            {
                EditorUtility.DisplayDialog("エラー",
                    "Assets フォルダ内に保存してください。", "OK");
                return;
            }

            string assetPath = "Assets" + savePath.Substring(dataPath.Length);

            byte[] pngData = previewTexture.EncodeToPNG();
            File.WriteAllBytes(savePath, pngData);
            AssetDatabase.ImportAsset(assetPath);

if (!string.IsNullOrEmpty(origPath))
            {
                CopyTextureImportSettings(origPath, assetPath);
            }

var savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (savedTex != null)
            {
                Undo.RecordObject(targetMaterial, "Apply Mole Texture");
                targetMaterial.SetTexture(texturePropertyName, savedTex);
                EditorUtility.SetDirty(targetMaterial);
                originalMainTex = savedTex;
            }

readableSource = MakeReadable(savedTex);
            placedMoles.Clear();
            previewTexture = null;

            Debug.Log($"[MoleTool] 保存完了: {assetPath}");
            EditorUtility.DisplayDialog("完了",
                $"テクスチャを保存しました。\n{assetPath}", "OK");
        }

        private static void CopyTextureImportSettings(string fromPath, string toPath)
        {
            var src = AssetImporter.GetAtPath(fromPath) as TextureImporter;
            var dst = AssetImporter.GetAtPath(toPath) as TextureImporter;
            if (src == null || dst == null) return;

            dst.textureType = src.textureType;
            dst.sRGBTexture = src.sRGBTexture;
            dst.alphaSource = src.alphaSource;
            dst.mipmapEnabled = src.mipmapEnabled;
            dst.maxTextureSize = src.maxTextureSize;
            dst.textureCompression = src.textureCompression;
            dst.streamingMipmaps = src.streamingMipmaps;
            dst.SaveAndReimport();
        }

private void SafeRestoreTexture()
        {
            if (targetMaterial == null || originalMainTex == null || texturePropertyName == null) return;

var current = targetMaterial.GetTexture(texturePropertyName);
            if (current == previewTexture && previewTexture != null)
            {
                targetMaterial.SetTexture(texturePropertyName, originalMainTex);
            }
        }

        private static Texture2D MakeReadable(Texture2D source)
        {
            if (source == null) return null;

            try
            {
                source.GetPixel(0, 0);
                return source;
            }
            catch { }

            RenderTexture rt = RenderTexture.GetTemporary(
                source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return readable;
        }
    }
}
