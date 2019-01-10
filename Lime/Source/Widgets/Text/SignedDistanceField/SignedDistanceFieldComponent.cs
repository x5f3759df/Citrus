using Yuzu;
using Lime.SignedDistanceField;
using System.Collections.Generic;

namespace Lime
{
	public class ShadowParams
	{
		public enum ShadowType
		{
			Outer,
			Inner
		}

		private const float MinimumSoftness = 0f;
		private const float MaximumSoftness = 45f;
		private const float MinimumDilate = -40f;
		private const float MaximumDilate = 40f;

		private float softness = 0f;
		private float dilate = 0f;

		[YuzuMember]
		public bool Enabled { get; set; } = true;

		[YuzuMember]
		public ShadowType Type { get; set; }

		[YuzuMember]
		public Color4 Color { get; set; } = Color4.Black;

		[YuzuMember]
		public Vector2 Offset { get; set; }

		[YuzuMember]
		public float Softness
		{
			get => softness;
			set => softness = Mathf.Clamp(value, MinimumSoftness, MaximumSoftness);
		}

		[YuzuMember]
		public float Dilate
		{
			get => dilate;
			set => dilate = Mathf.Clamp(value, MinimumDilate, MaximumDilate);
		}
	}

	[TangerineRegisterComponent]
	[AllowedComponentOwnerTypes(typeof(SimpleText), typeof(RichText))]
	public class SignedDistanceFieldComponent : NodeComponent
	{
		private const string GroupFont = "01. Face";
		private const string GroupOutline = "02. Outline";
		private const string GroupGradient = "03. Gradient";
		private const string GroupBevel = "04. Bevel";
		private const string GroupShadow = "05. Shadows";
		private const float MinimumSoftness = 0f;
		private const float MaximumSoftness = 50f;
		private const float MinimumDilate = -30f;
		private const float MaximumDilate = 30f;
		private const float MinimumThickness = 0f;
		private const float MaximumThickness = 30f;
		private const float MinimumLightAngle = 0f;
		private const float MaximumLightAngle = 360f;
		private const float MinimumReflectionPower = 0f;
		private const float MaximumReflectionPower = 100f;
		private const float MinimumBevelRoundness = 0f;
		private const float MaximumBevelRoundness = 5f;
		private const float MinimumBevelWidth = 0f;
		private const float MaximumBevelWidth = 30f;

		internal SDFMaterialProvider SDFMaterialProvider { get; private set; } = new SDFMaterialProvider();

		private SDFPresenter presenter = new SDFPresenter();
		private SDFRenderChainBuilder renderChainBuilder = new SDFRenderChainBuilder();
		private float softness = 0f;
		private float dilate = 0f;
		private float thickness = 0f;
		private float lightAngle;
		private float reflectionPower;
		private float bevelRoundness;
		private float bevelWidth;

		[YuzuMember]
		[TangerineGroup(GroupFont)]
		public float Softness
		{
			get => softness;
			set => softness = Mathf.Clamp(value, MinimumSoftness, MaximumSoftness);
		}

		[YuzuMember]
		[TangerineGroup(GroupFont)]
		public float Dilate
		{
			get => dilate;
			set => dilate = Mathf.Clamp(value, MinimumDilate, MaximumDilate);
		}

		[YuzuMember]
		[TangerineGroup(GroupOutline)]
		public Color4 OutlineColor { get; set; } = Color4.Black;

		[YuzuMember]
		[TangerineGroup(GroupOutline)]
		public float Thickness
		{
			get => thickness;
			set => thickness = Mathf.Clamp(value, MinimumThickness, MaximumThickness);
		}

		[YuzuMember]
		[TangerineGroup(GroupGradient)]
		public bool GradientEnabled { get; set; }

		[YuzuMember]
		[TangerineGroup(GroupGradient)]
		public ColorGradient Gradient { get; set; } = new ColorGradient(Color4.White, Color4.Black);

		[YuzuMember]
		[TangerineGroup(GroupGradient)]
		public float GradientAngle { get; set; }

		[YuzuMember]
		[TangerineGroup(GroupBevel)]
		public bool BevelEnabled { get; set; }

		[YuzuMember]
		[TangerineGroup(GroupBevel)]
		public Color4 LightColor { get; set; } = Color4.White;

		[YuzuMember]
		[TangerineGroup(GroupBevel)]
		public float LightAngle
		{
			get => lightAngle;
			set => lightAngle = Mathf.Clamp(value, MinimumLightAngle, MaximumLightAngle);
		}

		[YuzuMember]
		[TangerineGroup(GroupBevel)]
		public float ReflectionPower
		{
			get => reflectionPower;
			set => reflectionPower = Mathf.Clamp(value, MinimumReflectionPower, MaximumReflectionPower);
		}

		[YuzuMember]
		[TangerineGroup(GroupBevel)]
		public float BevelRoundness
		{
			get => bevelRoundness;
			set => bevelRoundness = Mathf.Clamp(value, MinimumBevelRoundness, MaximumBevelRoundness);
		}

		[YuzuMember]
		[TangerineGroup(GroupBevel)]
		public float BevelWidth
		{
			get => bevelWidth;
			set => bevelWidth = Mathf.Clamp(value, MinimumBevelWidth, MaximumBevelWidth);
		}

		[YuzuMember]
		[TangerineGroup(GroupShadow)]
		public List<ShadowParams> Shadows { get; set; }

		public void GetOwnerRenderObjects(RenderChain renderChain, RenderObjectList roObjects)
		{
			DettachFromNode(Owner);
			Owner.AddToRenderChain(renderChain);
			renderChain.GetRenderObjects(roObjects);
			AttachToNode(Owner);
		}

		protected override void OnOwnerChanged(Node oldOwner)
		{
			base.OnOwnerChanged(oldOwner);
			if (oldOwner != null) {
				DettachFromNode(oldOwner);
			}
			if (Owner != null) {
				AttachToNode(Owner);
			}
		}

		private void AttachToNode(Node node)
		{
			node.Presenter = presenter;
			node.RenderChainBuilder = renderChainBuilder;
			renderChainBuilder.Owner = node.AsWidget;
		}

		private void DettachFromNode(Node node)
		{
			node.RenderChainBuilder = node;
			node.Presenter = DefaultPresenter.Instance;
			renderChainBuilder.Owner = null;
		}

		public override NodeComponent Clone()
		{
			var clone = (SignedDistanceFieldComponent)base.Clone();
			clone.presenter = (SDFPresenter)presenter.Clone();
			clone.renderChainBuilder = (SDFRenderChainBuilder)renderChainBuilder.Clone(null);
			clone.SDFMaterialProvider = (SDFMaterialProvider)SDFMaterialProvider.Clone();
			return clone;
		}
	}
}
