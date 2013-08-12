﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProtoBuf;

namespace Lime
{
	[ProtoContract]
	public class TextBox : Widget
	{
		private string text;

		[ProtoMember(1)]
		public SerializableFont Font = new SerializableFont();

		[ProtoMember(2)]
		public override string Text { get { return text ?? ""; } set { text = value; } }

		[ProtoMember(3)]
		public int MaxTextLength = 50;

		[ProtoMember(4)]
		public float FontHeight = 15;

		[ProtoMember(5)]
		public float BorderWidth = 5;

		[ProtoMember(6)]
		public char CaretChar = '_';

		[ProtoMember(7)]
		public float CaretBlinkPeriod = 0.5f;

		[ProtoMember(8)]
		public bool Numeric;

		[ProtoMember(9)]
		public HAlignment HAlignment;

		int caretBlinkPhase;

		[ProtoMember(10)]
		public bool Enabled { get; set; }

		public TextBox()
		{
			Enabled = true;
		}

		public override void AddToRenderChain(RenderChain chain)
		{
			if (GloballyVisible) {
				int oldLayer = chain.SetLayer(Layer);
				chain.Add(this);
				foreach (Node node in Nodes.AsArray) {
					node.AddToRenderChain(chain);
				}
				chain.SetLayer(oldLayer);
			}
		}

		public override void Update(int delta)
		{
			var world = World.Instance;
			if (!Enabled) {
				if (world.ActiveTextWidget == this) {
					world.ActiveTextWidget = null;
				}
				base.Update(delta);
			} else {
				if (world.ActiveTextWidget == null) {
					world.ActiveTextWidget = this;
				}
				if (Input.WasKeyPressed(Key.Mouse0) && HitTest(Input.MousePosition)) {
					world.ActiveTextWidget = this;
				}
				base.Update(delta);
				if (world.ActiveTextWidget == this && Input.TextInput != null) {
					ProcessInput();
				}
				if (world.ActiveTextWidget == this) {
					world.IsActiveTextWidgetUpdated = true;
				}
				caretBlinkPhase += delta;
			}
		}

		private void ProcessInput()
		{
			foreach (char c in Input.TextInput) {
				if (c >= 32 && Text.Length < MaxTextLength) {
					if (Numeric) {
						float foo;
						if ((c == '-' && Text == "") || float.TryParse(Text + c, out foo)) {
							Text += c;
						}
					} else {
						Text += c;
					}
				} else if (Text.Length > 0 && c == 8) {
					Text = Text.Remove(Text.Length - 1);
				}
			}
		}

		public override void Render()
		{
			string text = Text;
			if (World.Instance.ActiveTextWidget == this) {
				text += CaretChar;
			}
			int start = 0;
			int length = text.Length;
			Vector2 extent = Renderer.MeasureTextLine(Font.Instance, text, FontHeight);
			while (extent.X > Width - BorderWidth * 2 && length > 0) {
				start++;
				length--;
				extent = Renderer.MeasureTextLine(Font.Instance, text, FontHeight, start, length);
			}
			int period = (int)(CaretBlinkPeriod * 1000);
			if (World.Instance.ActiveTextWidget == this) {
				if (caretBlinkPhase % period < period / 2 && length > 0) {
					length--;
				}
			}
			var textPosition = new Vector2(BorderWidth, (Height - FontHeight) / 2);
			if (HAlignment == HAlignment.Right)
				textPosition.X = Size.X - BorderWidth - extent.X;
			else if (HAlignment == HAlignment.Center)
				textPosition.X = (Size.X - extent.X) * 0.5f;
			Renderer.Transform1 = LocalToWorldTransform;
			Renderer.Blending = GlobalBlending;
			Renderer.DrawTextLine(Font.Instance, textPosition, text, GlobalColor, FontHeight, start, length);
		}
	}
}
