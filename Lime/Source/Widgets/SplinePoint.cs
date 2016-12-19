using Yuzu;

namespace Lime
{
	public class SplinePoint : PointObject
	{
		[YuzuMember]
		public bool Straight { get; set; }

		[YuzuMember]
		public float TangentAngle { get; set; }

		[YuzuMember]
		public float TangentWeight { get; set; }

		public SplinePoint()
		{
			TangentWeight = 1.0f;
		}
	}
}