﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor.Experimental.UIElements;
using UnityEngine.Experimental.UIElements;
using UnityEditor.Graphing;
using UnityEngine.Experimental.UIElements.StyleSheets;
using Object = UnityEngine.Object;

namespace UnityEditor.ShaderGraph.Drawing.Inspector
{
    public class MasterPreviewView : VisualElement
    {
        PreviewManager m_PreviewManager;
        AbstractMaterialGraph m_Graph;

        PreviewRenderData m_PreviewRenderHandle;
        Image m_PreviewTextureView;

        Vector2 m_PreviewScrollPosition;
        ObjectField m_PreviewMeshPicker;

        IMasterNode m_MasterNode;
        Mesh m_PreviousMesh;

        List<string> m_DoNotShowPrimitives = new List<string>( new string[] {PrimitiveType.Plane.ToString()});

        static Type s_ContextualMenuManipulator = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).FirstOrDefault(t => t.FullName == "UnityEngine.Experimental.UIElements.ContextualMenuManipulator");
        static Type s_ObjectSelector = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).FirstOrDefault(t => t.FullName == "UnityEditor.ObjectSelector");

        public MasterPreviewView(string assetName, PreviewManager previewManager, AbstractMaterialGraph graph)
        {
            this.clippingOptions = ClippingOptions.ClipAndCacheContents;
            m_PreviewManager = previewManager;
            m_Graph = graph;

            AddStyleSheetPath("Styles/MasterPreviewView");

            m_PreviewRenderHandle = previewManager.masterRenderData;

            var topContainer = new VisualElement() { name = "top" };
            {
                var title = new Label(assetName.Split('/').Last()) { name = "title" };
                topContainer.Add(title);
            }
            Add(topContainer);

            var middleContainer = new VisualElement {name = "middle"};
            {
                m_PreviewTextureView = CreatePreview(Texture2D.blackTexture);
                m_PreviewScrollPosition = new Vector2(0f, 0f);
                middleContainer.Add(m_PreviewTextureView);
                middleContainer.AddManipulator(new Scrollable(OnScroll));
            }
            m_PreviewRenderHandle.onPreviewChanged += OnPreviewChanged;
            Add(middleContainer);
        }

        Image CreatePreview(Texture texture)
        {
            var image = new Image { name = "preview", image = StyleValue<Texture>.Create(m_PreviewRenderHandle.texture ?? texture) };
            image.AddManipulator(new Draggable(OnMouseDragPreviewMesh, true));
            image.AddManipulator((IManipulator)Activator.CreateInstance(s_ContextualMenuManipulator, (Action<ContextualMenuPopulateEvent>)BuildContextualMenu));
            return image;
        }

        void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            foreach (var primitiveTypeName in Enum.GetNames(typeof(PrimitiveType)))
            {
                if(m_DoNotShowPrimitives.Contains(primitiveTypeName))
                    continue;
                evt.menu.AppendAction(primitiveTypeName, e => ChangePrimitiveMesh( primitiveTypeName ), ContextualMenu.MenuAction.AlwaysEnabled);
            }

            evt.menu.AppendAction("Custom Mesh", e => ChangeMeshCustom(), ContextualMenu.MenuAction.AlwaysEnabled);
        }

        IMasterNode masterNode
        {
            get { return m_PreviewRenderHandle.shaderData != null ? m_PreviewRenderHandle.shaderData.node as IMasterNode : null; }
        }

        void DirtyMasterNode(ModificationScope scope)
        {
            var amn = masterNode as AbstractMaterialNode;
            if (amn != null)
                amn.Dirty(scope);
        }

        void OnPreviewChanged()
        {
            m_PreviewTextureView.image = StyleValue<Texture>.Create(m_PreviewRenderHandle.texture ?? Texture2D.blackTexture);
            m_PreviewTextureView.Dirty(ChangeType.Repaint);
        }

        void ChangePrimitiveMesh(string primitiveName)
        {
            Mesh changedPrimitiveMesh = Resources.GetBuiltinResource(typeof(Mesh), string.Format("{0}.fbx", primitiveName)) as Mesh;

            ChangeMesh(changedPrimitiveMesh);
        }

        void ChangeMesh(Mesh mesh)
        {
            Mesh changedMesh = mesh;

            DirtyMasterNode(ModificationScope.Node);

            if (m_Graph.previewData.serializedMesh.mesh != changedMesh)
            {
                m_Graph.previewData.rotation = Quaternion.identity;
            }

            m_Graph.previewData.serializedMesh.mesh = changedMesh;
        }

        private static EditorWindow Get()
        {
            PropertyInfo P = s_ObjectSelector.GetProperty("get", BindingFlags.Public | BindingFlags.Static);
            return P.GetValue(null,null) as EditorWindow;
        }

        void OnMeshChanged(Object obj)
        {
            var mesh = obj as Mesh;
            if (mesh == null)
                mesh = m_PreviousMesh;
            ChangeMesh(mesh);
        }

        void ChangeMeshCustom()
        {
            MethodInfo ShowMethod = s_ObjectSelector.GetMethod("Show", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, Type.DefaultBinder, new[] {typeof(Object), typeof(Type), typeof(SerializedProperty), typeof(bool), typeof(List<int>), typeof(Action<Object>), typeof(Action<Object>)}, new ParameterModifier[7]);
            m_PreviousMesh = m_Graph.previewData.serializedMesh.mesh;
            ShowMethod.Invoke(Get(), new object[] { null, typeof(Mesh), null, false, null, (Action<Object>)OnMeshChanged, (Action<Object>)OnMeshChanged });
        }

        public void RefreshRenderTextureSize()
        {
            var currentWidth = m_PreviewRenderHandle.texture != null ? m_PreviewRenderHandle.texture.width : -1;
            var currentHeight = m_PreviewRenderHandle.texture != null ? m_PreviewRenderHandle.texture.height : -1;

            var targetWidth = m_PreviewTextureView.contentRect.width;
            var targetHeight = m_PreviewTextureView.contentRect.height;

            if (Mathf.Approximately(currentWidth, targetHeight) && Mathf.Approximately(currentHeight, targetWidth))
                return;

            m_PreviewManager.ResizeMasterPreview(m_PreviewTextureView.contentRect.size);
        }

        public void UpdateRenderTextureOnNextLayoutChange()
        {
            RegisterCallback<PostLayoutEvent>(AdaptRenderTextureOnLayoutChange);
        }

        void AdaptRenderTextureOnLayoutChange(PostLayoutEvent evt)
        {
            UnregisterCallback<PostLayoutEvent>(AdaptRenderTextureOnLayoutChange);
            RefreshRenderTextureSize();
        }

        void OnScroll(float scrollValue)
        {
            float rescaleAmount = -scrollValue * .03f;
            m_Graph.previewData.scale = Mathf.Clamp(m_Graph.previewData.scale + rescaleAmount, 0.2f, 5f);

            DirtyMasterNode(ModificationScope.Node);
        }

        void OnMouseDragPreviewMesh(Vector2 deltaMouse)
        {
            Vector2 previewSize = m_PreviewTextureView.contentRect.size;

            m_PreviewScrollPosition -= deltaMouse * (Event.current.shift ? 3f : 1f) / Mathf.Min(previewSize.x, previewSize.y) * 140f;
            m_PreviewScrollPosition.y = Mathf.Clamp(m_PreviewScrollPosition.y, -90f, 90f);
            Quaternion previewRotation = Quaternion.Euler(m_PreviewScrollPosition.y, 0, 0) * Quaternion.Euler(0, m_PreviewScrollPosition.x, 0);
            m_Graph.previewData.rotation = previewRotation;

            DirtyMasterNode(ModificationScope.Node);
        }
    }
}
