using System;
using ProtoBuf;

namespace Lime
{
	[ProtoContract]
	public class Slider : Widget
	{
		[ProtoMember(1)]
		public float RangeMin { get; set; }

		[ProtoMember(2)]
		public float RangeMax { get; set; }

		[ProtoMember(3)]
		public float Value
		{
			get { return value.Clamp(RangeMin, RangeMax); }
			set { this.value = value; }
		}

		public event Action Changed;
		public bool Enabled;

		private float value;
		private Widget thumb;
		private Spline rail;

		public Slider()
		{
			RangeMin = 0;
			RangeMax = 100;
			Value = 0;
			Enabled = true;
		}

		private Widget Thumb
		{
			get {
				if (thumb == null) {
					thumb = Nodes.TryFind("Thumb") as Widget;
				}
				if (thumb == null) {
					thumb = Nodes.TryFind("SliderThumb") as Widget;
				}
				return thumb;
			}
		}

		private Spline Rail
		{
			get {
				if (rail == null) {
					rail = Nodes.TryFind("Rail") as Spline;
				}
				return rail;
			}
		}

		public override void Update(int delta)
		{
			if (GloballyVisible) {
				Advance();
			}
			base.Update(delta);
		}

		void Advance()
		{
			if (Thumb == null) {
				return;
			}
			if (Input.WasMousePressed() && Thumb.IsMouseOver()) {
				TryRunAnimation("Press");
				Input.CaptureMouse();
			} else if (Input.IsMouseOwner() && !Input.IsMousePressed()) {
				Release();
			}
			if (Input.IsMouseOwner() && Enabled) {
				if (Input.WasMousePressed()) {
					SetValueFromCurrentMousePosition(draggingJustBegun: true);
				} else if (Input.IsMousePressed()) {
					SetValueFromCurrentMousePosition(draggingJustBegun: false);
				}
			}
			RefreshThumbPosition();
			if (!Input.IsMouseOwner()) {
				Release();
			}
		}

		private void RefreshThumbPosition()
		{
			if (RangeMax > RangeMin) {
				var t = (Value - RangeMin) / (RangeMax - RangeMin);
				var pos = Rail.CalcPoint(t * Rail.CalcLengthRough());
				Thumb.Position = Rail.CalcTransitionToSpaceOf(this) * pos;
			}
		}

		private void Release()
		{
			TryRunAnimation("Normal");
			if (Input.IsMouseOwner()) {
				Input.ReleaseMouse();
			}
		}

		float dragInitialOffset;
		float dragInitialDelta;

		private void SetValueFromCurrentMousePosition(bool draggingJustBegun)
		{
			if (Rail == null) {
				return;
			}
			float railLength = Rail.CalcLengthRough();
			if (railLength <= 0) {
				return;
			}
			Matrix32 transform = Rail.LocalToWorldTransform.CalcInversed();
			Vector2 p = transform.TransformVector(Input.MousePosition);
			float offset = Rail.CalcSplineLengthToNearestPoint(p) / railLength;
			if (RangeMax <= RangeMin) {
				return;
			}
			float v = offset * (RangeMax - RangeMin) + RangeMin;
			if (draggingJustBegun) {
				dragInitialDelta = Value - v;
				dragInitialOffset = offset;
				return;
			}
			float prevValue = Value;
			if (offset > dragInitialOffset && dragInitialOffset < 1) {
				Value = v + dragInitialDelta * (1 - (offset - dragInitialOffset) / (1 - dragInitialOffset));
			} else if (offset < dragInitialOffset && dragInitialOffset > 0) {
				Value = v + dragInitialDelta * (1 - (dragInitialOffset - offset) / dragInitialOffset);
			} else {
				Value = v + dragInitialDelta;
			}
			if (Changed != null && Value != prevValue) {
				Changed();
			}
		}
	}
}
