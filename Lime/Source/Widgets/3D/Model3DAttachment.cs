using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Yuzu;

namespace Lime
{
	public class Model3DAttachment
	{
		public const string FileExtension = ".Attachment.txt";

		public readonly ObservableCollection<MeshOption> MeshOptions = new ObservableCollection<MeshOption>();
		public readonly ObservableCollection<Animation> Animations = new ObservableCollection<Animation>();
		public readonly List<string> SourceAnimationIds = new List<string>();
		public readonly ObservableCollection<NodeComponentCollection> NodeComponents = new ObservableCollection<NodeComponentCollection>();
		public readonly ObservableCollection<NodeRemoval> NodeRemovals = new ObservableCollection<NodeRemoval>();

		public float ScaleFactor { get; set; }

		public class MeshOption
		{
			public string Id { get; set; }
			public bool HitTestTarget { get; set; }
			public CullMode CullMode { get; set; }
			public bool Opaque { get; set; }
		}

		public class Animation
		{
			public string Name { get; set; }
			public int StartFrame { get; set; } = 0;
			public int LastFrame { get; set; } = -1;
			public string SourceAnimationId { get; set; }
			public ObservableCollection<MarkerData> Markers = new ObservableCollection<MarkerData>();
			public ObservableCollection<NodeData> Nodes = new ObservableCollection<NodeData>();
			public ObservableCollection<NodeData> IgnoredNodes = new ObservableCollection<NodeData>();
			public BlendingOption Blending { get; set; }
			public readonly ObservableCollection<MarkerBlendingData> MarkersBlendings = new ObservableCollection<MarkerBlendingData>();
		}

		public class NodeData
		{
			public string Id { get; set; }
		}

		public class MarkerData
		{
			public Marker Marker { get; set; }
			public BlendingOption Blending { get; set; }
		}

		public class MarkerBlendingData
		{
			public string SourceMarkerId { get; set; }
			public string DestMarkerId { get; set; }
			public BlendingOption Blending { get; set; } = new BlendingOption();
		}

		public class NodeComponentCollection
		{
			public string NodeId { get; set; }
			public ObservableCollection<NodeComponent> Components { get; set; }
		}

		public class NodeRemoval
		{
			public string NodeId { get; set; }
		}

		public void Apply(Node3D model)
		{
			ProcessMeshOptions(model);
			ProcessAnimations(model);
			ProcessComponents(model);
			ProcessNodeRemovals(model);
			ApplyScaleFactor(model);
		}

		private void ProcessComponents(Node3D model)
		{
			foreach (var nodeComponentData in NodeComponents) {
				Node node;
				if ((node = model.TryFindNode(nodeComponentData.NodeId)) != null) {
					foreach (var component in nodeComponentData.Components) {
						if (ValidateComponentType(node.GetType(), component.GetType())) {
							node.Components.Add(component.Clone());
						} else {
							Console.WriteLine($"Warning: Unable to add {component.GetType().Name} to the {node.Id}." +
								" This component type isn't allowed for this node type.");
						}
					}
				}
			}
		}

		public static bool ValidateComponentType(Type nodeType, Type componentType)
		{
			for (var t = componentType; t != null && t != typeof(NodeComponentCollection); t = t.BaseType) {
				var a = componentType.GetCustomAttributes(false).OfType<AllowedComponentOwnerTypes>().FirstOrDefault();
				if (a != null) {
					return a.Types.Any(ownerType => ownerType == nodeType || nodeType.IsSubclassOf(ownerType));
				}
			}
			return true;
		}

		private void ApplyScaleFactor(Node3D model)
		{
			if (ScaleFactor == 1) {
				return;
			}
			var sf = Vector3.One * ScaleFactor;
			var nodes = model.Descendants.OfType<Node3D>();
			foreach (var node in nodes) {
				node.Position *= sf;
				if (node is Mesh3D) {
					foreach (var submesh in (node as Mesh3D).Submeshes) {
						var vertices = submesh.Mesh.Vertices;
						for (var i = 0; i < vertices.Length; i++) {
							vertices[i].Pos *= sf;
						}
						submesh.Mesh.DirtyFlags |= MeshDirtyFlags.Vertices;
						for (var i = 0; i < submesh.BoneBindPoses.Count; i++) {
							submesh.BoneBindPoses[i].Decompose(out var scale, out Quaternion rotation, out var tranlation);
							tranlation *= sf;
							submesh.BoneBindPoses[i] =
								Matrix44.CreateRotation(rotation) *
								Matrix44.CreateScale(scale) *
								Matrix44.CreateTranslation(tranlation);
						}
					}
				} else if (node is Camera3D cam) {
					cam.NearClipPlane *= ScaleFactor;
					cam.FarClipPlane *= ScaleFactor;
					cam.OrthographicSize *= ScaleFactor;
				}
				foreach (var animator in node.Animators) {
					if (animator.TargetPropertyPath == "Position") {
						foreach (Keyframe<Vector3> key in animator.Keys) {
							key.Value *= sf;
						}
					}
				}
			}
		}

		private void ProcessMeshOptions(Node3D model)
		{
			if (MeshOptions.Count == 0) {
				return;
			}

			var meshes = model.Descendants
				.OfType<Mesh3D>()
				.Where(d => !string.IsNullOrEmpty(d.Id));
			foreach (var mesh in meshes) {
				foreach (var meshOption in MeshOptions) {
					if (mesh.Id != meshOption.Id) {
						continue;
					}

					if (meshOption.HitTestTarget) {
						mesh.HitTestTarget = true;
						mesh.SkipRender = true;
					}
					mesh.Opaque = meshOption.Opaque;
					mesh.CullMode = meshOption.CullMode;
					break;
				}
			}
		}

		private struct NodeAndAnimator
		{
			public Node Node;
			public IAnimator Animator;
		}

		private void ProcessAnimations(Node3D model)
		{
			if (Animations.Count == 0) {
				return;
			}

			var newAnimators = new List<NodeAndAnimator>();
			var newAnimations = new List<Lime.Animation>();
			var animationsToReduce = new Dictionary<string, (int, int)>();
			foreach (var animation in Animations) {
				if (!model.Animations.TryFind(animation.SourceAnimationId, out var srcAnimation)) {
					if (string.IsNullOrEmpty(animation.SourceAnimationId)) {
						// If no source animation id is present, it means that model attachment has obsolete format.
						srcAnimation = model.FirstAnimation;
						// Check if the animation has been deleted but attachment hasn't been modified after that.
						if (srcAnimation == null) {
#if TANGERINE
							Console.WriteLine($"Attachment3D Warning: skip '{ animation.Name }' animation applying. Source Fbx file have no animation data.");
#endif // TANGERINE
							continue;
						}
					} else {
#if TANGERINE
						Console.WriteLine($"Attachment3D Warning: source animation '{ animation.SourceAnimationId }' not found");
#endif // TANGERINE
						continue;
					}
				}

				var animationId = animation.Name;
				// TODO: Replace 'Default' animation with its source animation id in all referring to Lime projects.
				if (animationId == "Default") {
					animationId = srcAnimation.Id;
				}

				var newAnimation = srcAnimation;
				if (animationId != animation.SourceAnimationId && !string.IsNullOrEmpty(animation.SourceAnimationId)) {
					newAnimation = new Lime.Animation {
						Id = animationId
					};
					newAnimations.Add(newAnimation);
					model.Animations.Add(newAnimation);
					foreach (var node in GetAnimationNodes(model, animation)) {
						foreach (var animator in node.Animators) {
							if (animator.AnimationId == srcAnimation.Id) {
								var newAnimator = animator.Clone();
								animator.AnimationId = newAnimation.Id;
								CopyKeys(animator, newAnimator, animation.StartFrame, animation.LastFrame);
								if (newAnimator.Keys.Count > 0) {
									newAnimators.Add(new NodeAndAnimator {
										Node = node,
										Animator = newAnimator
									});
								}
							}
						}
					}
				} else {
					animationsToReduce.Add(newAnimation.Id, (animation.StartFrame, animation.LastFrame));
				}

				var animationBlending = new AnimationBlending() {
					Option = animation.Blending
				};

				foreach (var data in animation.Markers) {
					newAnimation.Markers.AddOrdered(data.Marker.Clone());
					if (data.Blending != null) {
						animationBlending.MarkersOptions.Add(
							data.Marker.Id,
							new MarkerBlending {
								Option = data.Blending
							});
					}
				}

				foreach (var markersBlendings in animation.MarkersBlendings) {
					if (!animationBlending.MarkersOptions.ContainsKey(markersBlendings.DestMarkerId)) {
						animationBlending.MarkersOptions.Add(markersBlendings.DestMarkerId, new MarkerBlending());
					}
					animationBlending.MarkersOptions[markersBlendings.DestMarkerId].SourceMarkersOptions
						.Add(markersBlendings.SourceMarkerId, markersBlendings.Blending);
				}

				if (animationBlending.Option != null || animationBlending.MarkersOptions.Count > 0) {
					model.Components.GetOrAdd<AnimationBlender>().Options.Add(newAnimation.Id ?? "", animationBlending);
				}

				foreach (var i in newAnimators) {
					i.Node.Animators.Add(i.Animator);
				}
				newAnimators.Clear();
			}

			foreach (var animation in model.Animations.Except(newAnimations)) {
				var srcAnimators = new List<IAnimator>();
				animation.FindAnimators(srcAnimators);
				if (animationsToReduce.Keys.Contains(animation.Id)) {
					animation.FindAnimators(srcAnimators);
					srcAnimators.ForEach(a => ReduceKeys(a, animationsToReduce[animation.Id].Item1, animationsToReduce[animation.Id].Item2));
					foreach (var animator in srcAnimators.Where(a => a.Keys.Count == 0)) {
						animator.Owner.Animators.Remove(animator);
					}
				} else {
					foreach (var animator in srcAnimators) {
						animator.Owner.Animators.Remove(animator);
					}
					model.Animations.Remove(animation);
				}
			}
		}

		private static void CopyKeys(IAnimator srcAnimator, IAnimator dstAnimator, int startFrame, int lastFrame)
		{
			if (!GetEffectiveKeyRange(srcAnimator, startFrame, lastFrame, out var startKeyIndex, out var lastKeyIndex)) {
				return;
			}
			if (startFrame != srcAnimator.ReadonlyKeys[startKeyIndex].Frame) {
				dstAnimator.Keys.Add(startFrame, srcAnimator.CalcValue(AnimationUtils.FramesToSeconds(startFrame)));
			}
			for (var i = startKeyIndex; i <= lastKeyIndex; i++) {
				dstAnimator.Keys.Add(srcAnimator.ReadonlyKeys[i]);
			}
			if (startFrame != lastFrame && lastFrame != srcAnimator.ReadonlyKeys[lastKeyIndex].Frame) {
				dstAnimator.Keys.Add(lastFrame, srcAnimator.CalcValue(AnimationUtils.FramesToSeconds(lastFrame)));
			}
		}

		private static void ReduceKeys(IAnimator animator, int startFrame, int lastFrame)
		{
			if (!GetEffectiveKeyRange(animator, startFrame, lastFrame, out var startKeyIndex, out var lastKeyIndex)) {
				return;
			}
			var readonlyKeys = animator.ReadonlyKeys;
			for (var i = 0; i < startKeyIndex; i++) {
				animator.Keys.Remove(readonlyKeys[i]);
			}
			for (var i = readonlyKeys.Count - 1; i > lastKeyIndex; i--) {
				animator.Keys.Remove(readonlyKeys[i]);
			}
		}

		private static bool GetEffectiveKeyRange(IAnimator animator, int startFrame, int lastFrame, out int startKeyIndex, out int lastKeyIndex)
		{
			startKeyIndex = -1;
			lastKeyIndex = -1;
			if (animator.ReadonlyKeys.Count == 0) {
				return false;
			}
			if (startFrame < 0) {
				startFrame = 0;
			}
			if (lastFrame < 0) {
				lastFrame = animator.ReadonlyKeys[animator.ReadonlyKeys.Count - 1].Frame;
			}
			for (var i = 0; i < animator.ReadonlyKeys.Count; i++) {
				if (startFrame <= animator.ReadonlyKeys[i].Frame) {
					startKeyIndex = i;
					break;
				}
			}
			for (var i = animator.ReadonlyKeys.Count - 1; i >= 0; i--) {
				if (lastFrame >= animator.ReadonlyKeys[i].Frame) {
					lastKeyIndex = i;
					break;
				}
			}
			return !(startKeyIndex == -1 || lastKeyIndex == -1);
		}

		private void ProcessNodeRemovals(Node3D model)
		{
			var nodes = NodeRemovals.SelectMany(removal => model.Descendants.Where(node => string.Equals(removal.NodeId, node.Id))).ToList();
			foreach (var node in nodes) {
				node.Unlink();
			}
		}

		private IEnumerable<Node> GetAnimationNodes(Node3D model, Animation animationData)
		{
			if (animationData.Nodes.Count > 0) {
				return animationData.Nodes.Distinct().Select(n => model.FindNode(n.Id));
			}
			if (animationData.IgnoredNodes.Count > 0) {
				var ignoredNodes = new HashSet<Node>(animationData.IgnoredNodes.Select(n => model.FindNode(n.Id)));
				return model.Descendants.Where(i => !ignoredNodes.Contains(i));
			}
			return model.Descendants;
		}
	}

	public class Model3DAttachmentParser
	{
		public enum UVAnimationType
		{
			Rotation,
			Offset
		}

		public enum UVAnimationOverlayBlending
		{
			Multiply,
			Overlay,
			Add
		}

		public class ModelAttachmentFormat
		{
			[YuzuMember]
			public Dictionary<string, MeshOptionFormat> MeshOptions = null;

			[YuzuMember]
			public List<string> SourceAnimationIds = null;

			[YuzuMember]
			public Dictionary<string, ModelAnimationFormat> Animations = null;

			[YuzuMember]
			public Dictionary<string, ModelComponentsFormat> NodeComponents = null;

			[YuzuMember]
			public List<string> NodeRemovals = null;

			[YuzuMember]
			public List<UVAnimationFormat> UVAnimations = null;

			[YuzuMember]
			public float ScaleFactor = 1f;
		}

		public class MeshOptionFormat
		{
			[YuzuMember]
			public bool HitTestTarget = false;

			[YuzuMember]
			public bool Opaque = false;

			[YuzuMember]
			public string CullMode = null;
		}

		public class UVAnimationFormat
		{
			[YuzuMember]
			public string MeshName = null;

			[YuzuMember]
			public string DiffuseTexture = null;

			[YuzuMember]
			public string OverlayTexture = null;

			[YuzuMember]
			public string MaskTexture = null;

			[YuzuMember]
			public float AnimationSpeed = 0;

			[YuzuMember]
			public UVAnimationType AnimationType = UVAnimationType.Rotation;

			[YuzuMember]
			public UVAnimationOverlayBlending BlendingMode = UVAnimationOverlayBlending.Multiply;

			[YuzuMember]
			public bool AnimateOverlay = false;

			[YuzuMember]
			public float TileX = 1f;

			[YuzuMember]
			public float TileY = 1f;
		}

		public class ModelAnimationFormat
		{
			[YuzuMember]
			[YuzuSerializeIf(nameof(ShouldSerializeStartFrame))]
			public int StartFrame = 0;

			[YuzuMember]
			[YuzuSerializeIf(nameof(ShouldSerializeLastFrame))]
			public int LastFrame = -1;

			[YuzuMember]
			public List<string> Nodes = null;

			[YuzuMember]
			public List<string> IgnoredNodes = null;

			[YuzuMember]
			public Dictionary<string, ModelMarkerFormat> Markers = null;

			[YuzuMember]
			public int? Blending = null;

			[YuzuMember]
			public string SourceAnimationId = null;

			public bool ShouldSerializeStartFrame() => StartFrame > 0;
			public bool ShouldSerializeLastFrame() => LastFrame >= 0;
		}

		public class ModelComponentsFormat
		{
			[YuzuMember]
			public string Node = null;

			[YuzuMember]
			public List<NodeComponent> Components = null;
		}

		public class ModelMarkerFormat
		{
			[YuzuMember]
			public int Frame = 0;

			[YuzuMember]
			public string Action = null;

			[YuzuMember]
			public string JumpTarget = null;

			[YuzuMember]
			public Dictionary<string, int> SourceMarkersBlending = null;

			[YuzuMember]
			public int? Blending = null;
		}

		private static string GetAttachmentPath(string modelPath)
		{
			return AssetPath.CorrectSlashes(Path.ChangeExtension(modelPath, Model3DAttachment.FileExtension));
		}

		public static bool IsAttachmentExists(string modelPath)
		{
			return File.Exists(GetAttachmentPath(modelPath));
		}

		public static ModelAttachmentFormat ReadModelAttachmentFormat(string modelPath)
		{
			return Serialization.ReadObjectFromFile<ModelAttachmentFormat>(GetAttachmentPath(modelPath));
		}

		public static Model3DAttachment GetModel3DAttachment(string modelPath)
		{
			return GetModel3DAttachment(ReadModelAttachmentFormat(modelPath), modelPath);
		}

		public static Model3DAttachment GetModel3DAttachment(ModelAttachmentFormat modelAttachmentFormat, string modelPath)
		{
			try {
				var attachment = new Model3DAttachment {
					ScaleFactor = modelAttachmentFormat.ScaleFactor
				};
				if (modelAttachmentFormat.MeshOptions != null) {
					foreach (var meshOptionFormat in modelAttachmentFormat.MeshOptions) {
						var meshOption = new Model3DAttachment.MeshOption {
							Id = meshOptionFormat.Key,
							HitTestTarget = meshOptionFormat.Value.HitTestTarget,
							Opaque = meshOptionFormat.Value.Opaque
						};
						if (!string.IsNullOrEmpty(meshOptionFormat.Value.CullMode)) {
							switch (meshOptionFormat.Value.CullMode) {
								case "None":
									meshOption.CullMode = CullMode.None;
									break;
								case "CullClockwise":
									meshOption.CullMode = CullMode.Front;
									break;
								case "CullCounterClockwise":
									meshOption.CullMode = CullMode.Back;
									break;
							}
						}
						attachment.MeshOptions.Add(meshOption);
					}
				}

				if (modelAttachmentFormat.NodeComponents != null) {
					foreach (var nodeComponentFormat in modelAttachmentFormat.NodeComponents) {
						var componentDescr = new Model3DAttachment.NodeComponentCollection {
							NodeId = nodeComponentFormat.Key,
							Components = new ObservableCollection<NodeComponent>(nodeComponentFormat.Value.Components)
						};
						attachment.NodeComponents.Add(componentDescr);
					}
				}

				if (modelAttachmentFormat.SourceAnimationIds != null) {
					foreach (var sourceAnimationId in modelAttachmentFormat.SourceAnimationIds) {
						attachment.SourceAnimationIds.Add(sourceAnimationId);
					}
				}

				if (modelAttachmentFormat.Animations != null) {
					foreach (var animationFormat in modelAttachmentFormat.Animations) {
						var sourceAnimationId = modelAttachmentFormat.SourceAnimationIds == null ||
							modelAttachmentFormat.SourceAnimationIds.Any(id => id == animationFormat.Key)
								? null
								: modelAttachmentFormat.SourceAnimationIds.FirstOrDefault();
						var animation = new Model3DAttachment.Animation {
							Name = animationFormat.Key,
							StartFrame = animationFormat.Value.StartFrame,
							LastFrame = animationFormat.Value.LastFrame,
							SourceAnimationId = sourceAnimationId
						};

						if (animationFormat.Value.Markers != null) {
							foreach (var markerFormat in animationFormat.Value.Markers) {
								var markerData = new Model3DAttachment.MarkerData {
									Marker = new Marker {
										Id = markerFormat.Key,
										Frame = FixFrame(markerFormat.Value.Frame)
									}
								};
								if (!string.IsNullOrEmpty(markerFormat.Value.Action)) {
									switch (markerFormat.Value.Action) {
										case "Start":
											markerData.Marker.Action = MarkerAction.Play;
											break;
										case "Stop":
											markerData.Marker.Action = MarkerAction.Stop;
											break;
										case "Jump":
											markerData.Marker.Action = MarkerAction.Jump;
											markerData.Marker.JumpTo = markerFormat.Value.JumpTarget;
											break;
									}
								}
								if (markerFormat.Value.Blending != null) {
									markerData.Blending = new BlendingOption((int)markerFormat.Value.Blending);
								}
								if (markerFormat.Value.SourceMarkersBlending != null) {
									foreach (var elem in markerFormat.Value.SourceMarkersBlending) {
										animation.MarkersBlendings.Add(new Model3DAttachment.MarkerBlendingData {
											DestMarkerId = markerFormat.Key,
											SourceMarkerId = elem.Key,
											Blending = new BlendingOption(elem.Value),
										});
									}
								}

								animation.Markers.Add(markerData);
							}
						}

						if (animationFormat.Value.Blending != null) {
							animation.Blending = new BlendingOption((int)animationFormat.Value.Blending);
						}

						if (animationFormat.Value.Nodes != null) {
							animation.Nodes = new ObservableCollection<Model3DAttachment.NodeData>(
								animationFormat.Value.Nodes.Select(n => new Model3DAttachment.NodeData { Id = n }));
						}
						animation.SourceAnimationId = animationFormat.Value.SourceAnimationId;
						if (animationFormat.Value.IgnoredNodes != null && animationFormat.Value.IgnoredNodes.Count > 0) {
							if (animation.Nodes.Count > 0) {
								throw new Exception("Conflict between 'Nodes' and 'IgnoredNodes' in animation '{0}", animation.Name);
							}
							animation.IgnoredNodes = new ObservableCollection<Model3DAttachment.NodeData>(
								animationFormat.Value.IgnoredNodes.Select(n => new Model3DAttachment.NodeData { Id = n }));
						}

						attachment.Animations.Add(animation);
					}
				}

				if (modelAttachmentFormat.NodeRemovals != null) {
					foreach (var id in modelAttachmentFormat.NodeRemovals) {
						attachment.NodeRemovals.Add(new Model3DAttachment.NodeRemoval { NodeId = id });
					}
				}

				return attachment;
			} catch (System.Exception e) {
				throw new System.Exception(modelPath + ": " + e.Message, e);
			}
		}

		public static void Save(Model3DAttachment attachment, string path)
		{
			var attachmentPath = path + ".Attachment.txt";
			Serialization.WriteObjectToFile(attachmentPath, ConvertToModelAttachmentFormat(attachment), Serialization.Format.JSON);
		}

		public static ModelAttachmentFormat ConvertToModelAttachmentFormat(Model3DAttachment attachment)
		{
			var origin = new ModelAttachmentFormat();
			origin.ScaleFactor = attachment.ScaleFactor;
			if (attachment.MeshOptions.Count > 0) {
				origin.MeshOptions = new Dictionary<string, MeshOptionFormat>();
			}
			if (attachment.Animations.Count > 0) {
				origin.Animations = new Dictionary<string, ModelAnimationFormat>();
			}
			if (attachment.SourceAnimationIds.Count > 0) {
				origin.SourceAnimationIds = new List<string>();
			}
			if (attachment.NodeComponents.Count > 0) {
				origin.NodeComponents = new Dictionary<string, ModelComponentsFormat>();
			}
			if (attachment.NodeRemovals.Count > 0) {
				origin.NodeRemovals = new List<string>();
			}
			foreach (var meshOption in attachment.MeshOptions) {
				var meshOptionFormat = new MeshOptionFormat {
					HitTestTarget = meshOption.HitTestTarget,
					Opaque = meshOption.Opaque
				};
				switch (meshOption.CullMode) {
					case CullMode.None:
						meshOptionFormat.CullMode = "None";
						break;
					case CullMode.Front:
						meshOptionFormat.CullMode = "CullClockwise";
						break;
					case CullMode.Back:
						meshOptionFormat.CullMode = "CullCounterClockwise";
						break;
				}
				origin.MeshOptions.Add(meshOption.Id, meshOptionFormat);
			}

			foreach (var component in attachment.NodeComponents) {
				var componentFormat = new ModelComponentsFormat {
					Node = component.NodeId,
					Components = component.Components.ToList(),
				};
				origin.NodeComponents.Add(component.NodeId, componentFormat);
			}

			foreach (var removal in attachment.NodeRemovals) {
				origin.NodeRemovals.Add(removal.NodeId);
			}

			foreach (var sourceAnimationId in attachment.SourceAnimationIds) {
				origin.SourceAnimationIds.Add(sourceAnimationId);
			}
			foreach (var animation in attachment.Animations) {
				var animationFormat = new ModelAnimationFormat {
					StartFrame = animation.StartFrame,
					LastFrame = animation.LastFrame,
					Markers = new Dictionary<string, ModelMarkerFormat>(),
					SourceAnimationId = animation.SourceAnimationId
				};
				foreach (var markerData in animation.Markers) {
					var markerFormat = new ModelMarkerFormat {
						Frame = markerData.Marker.Frame
					};
					switch (markerData.Marker.Action) {
						case MarkerAction.Play:
							markerFormat.Action = "Start";
							break;
						case MarkerAction.Stop:
							markerFormat.Action = "Stop";
							break;
						case MarkerAction.Jump:
							markerFormat.Action = "Jump";
							markerFormat.JumpTarget = markerData.Marker.JumpTo;
							break;
					}
					if (animation.MarkersBlendings.Count > 0) {
						markerFormat.SourceMarkersBlending = new Dictionary<string, int>();
						foreach (var markerBlending in animation.MarkersBlendings.Where(m => m.DestMarkerId == markerData.Marker.Id)) {
							markerFormat.SourceMarkersBlending.Add(markerBlending.SourceMarkerId, (int)markerBlending.Blending.Frames);
						}
					}
					if (markerData.Blending != null) {
						markerFormat.Blending = (int)markerData.Blending.Frames;
					}
					animationFormat.Markers.Add(markerData.Marker.Id, markerFormat);
				}

				if (animation.Blending != null) {
					animationFormat.Blending = (int)animation.Blending.Frames;
				}

				if (animation.Nodes.Count > 0) {
					animationFormat.Nodes = animation.Nodes.Count > 0 ? animation.Nodes.Select(n => n.Id).ToList() : null;
				} else if (animation.IgnoredNodes.Count > 0) {
					animationFormat.IgnoredNodes = animation.IgnoredNodes.Select(n => n.Id).ToList();
				}
				origin.Animations.Add(animation.Name, animationFormat);
			}

			return origin;
		}

		private static string FixPath(string modelPath, string path)
		{
			var baseDir = Path.GetDirectoryName(modelPath);
			return AssetPath.CorrectSlashes(Path.Combine(AssetPath.CorrectSlashes(baseDir), AssetPath.CorrectSlashes(path)));
		}

		private static int FixFrame(int frame, double fps = 30)
		{
			return AnimationUtils.SecondsToFrames(frame / fps);
		}
	}
}
