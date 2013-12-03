﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lime
{
	public struct Transform
	{
		public Vector2 Position;
		public Vector2 Scale;
		public float Rotation;
		public Vector2 U;
		public Vector2 V;
	}

	public partial class Widget : Node
	{
		public Transform Transform
		{
			get
			{
				// Vector2 cs = Mathf.CosSin(Mathf.DegreesToRadians * Rotation);
				return new Transform {
					Position = Position,
					Rotation = Rotation,
					Scale = Scale,
					// U = new Vector2(cs.X, cs.Y),
					// V = new Vector2(-cs.Y, cs.X)
				};
			}
			set
			{
				Position = value.Position;
				Rotation = value.Rotation;
				Scale = value.Scale;
			}
		}

		public void MatchByPositionWith(Widget widget)
		{
			Position = widget.CalcPositionInSpaceOf(Parent.AsWidget);
		}

		public void RenderToTexture(ITexture texture)
		{
			if (Width > 0 && Height > 0) {
				texture.SetAsRenderTarget();
				Renderer.ClearRenderTarget(0, 0, 0, 0);
				var savedViewport = Renderer.Viewport;
				Renderer.Viewport = new WindowRect { X = 0, Y = 0, Width = texture.ImageSize.Width, Height = texture.ImageSize.Height };
				Renderer.PushProjectionMatrix();
				Renderer.SetOrthogonalProjection(0, Height, Width, 0);
				var chain = new RenderChain();
				foreach (var node in Nodes) {
					node.AddToRenderChain(chain);
				}
				chain.RenderAndClear();
				texture.RestoreRenderTarget();
				Renderer.Viewport = savedViewport;
				Renderer.PopProjectionMatrix();
			}
		}

		public static bool AreWidgetsIntersected(Widget a, Widget b)
		{
			Vector2[] rect = new Vector2[4] {
				new Vector2(0, 0), new Vector2(1, 0),
				new Vector2(1, 1), new Vector2(0, 1)
			};
			a.RecalcGlobalMatrixAndColor();
			b.RecalcGlobalMatrixAndColor();
			var sizes = new Vector2[2] { a.Size, b.Size };
			var matrices = new Matrix32[2] { a.LocalToWorldTransform, b.LocalToWorldTransform };
			var det = new float[2] { matrices[0].CalcDeterminant(), matrices[1].CalcDeterminant() };
			if (det[0] == 0 || det[1] == 0) {
				return false;
			}
			for (int k = 0; k < 2; k++)
				for (int i = 0; i < 4; i++) {
					var ptA = matrices[k] * (rect[i] * sizes[k]);
					var ptB = matrices[k] * (rect[(i + 1) % 4] * sizes[k]);
					var isOutside = true;
					for (int j = 0; j < 4; j++) {
						var pt = matrices[1 - k] * (rect[j] * sizes[1 - k]);
						isOutside = isOutside && Geometry.CalcPointHalfPlane(pt, ptA, ptB) * det[k] < 0;
					}
					if (isOutside)
						return false;
				}
			return true;
		}

		public void CenterOnParent()
		{
			if (Parent == null) {
				throw new Lime.Exception("Parent must not be null");
			}
			Position = Parent.AsWidget.Size * 0.5f;
			Pivot = Vector2.Half;
		}

		public Matrix32 CalcTransitionToSpaceOf(Widget container)
		{
			RecalcGlobalMatrixAndColor();
			container.RecalcGlobalMatrixAndColor();
			Matrix32 mtx1 = container.LocalToWorldTransform.CalcInversed();
			Matrix32 mtx2 = LocalToWorldTransform;
			return mtx2 * mtx1;
		}

		public Vector2[] CalcHullInSpaceOf(Widget container)
		{
			Vector2[] vertices = new Vector2[4];
			var transform = CalcTransformInSpaceOf(container);
			vertices[0] = transform.Position - transform.U * Size.X * Pivot.X - transform.V * Size.Y * Pivot.Y;
			vertices[1] = vertices[0] + transform.U * Size.X;
			vertices[2] = vertices[0] + transform.U * Size.X + transform.V * Size.Y;
			vertices[3] = vertices[0] + transform.V * Size.Y;
			return vertices;
		}

		public Rectangle CalcAABBInSpaceOf(Widget container)
		{
			var vertices = CalcHullInSpaceOf(container);
			var aabb = new Rectangle(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
			foreach (var v in vertices) {
				aabb.Left = Mathf.Min(v.X, aabb.Left);
				aabb.Right = Mathf.Max(v.X, aabb.Right);
				aabb.Top = Mathf.Min(v.Y, aabb.Top);
				aabb.Bottom = Mathf.Max(v.Y, aabb.Bottom);
			}
			return aabb;
		}

		public Transform CalcTransformFromMatrix(Matrix32 matrix)
		{
			var v1 = new Vector2(1, 0);
			var v2 = new Vector2(0, 1);
			var v3 = new Vector2(0, 0);
			v1 = matrix.TransformVector(v1);
			v2 = matrix.TransformVector(v2);
			v3 = matrix.TransformVector(v3);
			v1 = v1 - v3;
			v2 = v2 - v3;
			Transform transform;
			transform.Position = matrix.TransformVector(Pivot * Size);
			transform.Scale = new Vector2(v1.Length, v2.Length);
			transform.Rotation = v1.Atan2Deg;
			transform.U = v1;
			transform.V = v2;
			return transform;
		}

		public Transform CalcTransformInSpaceOf(Widget container)
		{
			Matrix32 matrix = CalcTransitionToSpaceOf(container);
			return CalcTransformFromMatrix(matrix);
		}

		public Vector2 CalcPositionInSpaceOf(Widget container)
		{
			return CalcTransformInSpaceOf(container).Position;
		}
	}
}
