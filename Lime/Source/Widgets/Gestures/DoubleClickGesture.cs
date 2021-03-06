using System;

namespace Lime
{
	public class DoubleClickGesture : Gesture
	{
		private enum State
		{
			Idle,
			FirstPress,
			WaitSecondPress,
		};

		private readonly float MaxDelayBetweenClicks =
#if WIN
			(float)TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime).TotalSeconds;
#else // WIN
			0.3f;
#endif // WIN

		private readonly Vector2 DoubleClickThreshold =
#if WIN
			new Vector2(
				System.Windows.Forms.SystemInformation.DoubleClickSize.Width,
				System.Windows.Forms.SystemInformation.DoubleClickSize.Height
			) / 2f;
#else // WIN
			new Vector2(5f, 5f) / 2f;
#endif // WIN


		private State state;
		private float timeSinceFirstPress;
		private Vector2 firstPressPosition;

		public override bool IsActive => state != State.Idle;

		public int ButtonIndex { get; }

		public DoubleClickGesture() : this(0, null)
		{
		}

		public DoubleClickGesture(int buttonIndex) : this(buttonIndex, null)
		{
		}

		public DoubleClickGesture(Action onRecognized) : this(0, onRecognized)
		{
		}

		public DoubleClickGesture(int buttonIndex, Action onRecognized) : base(onRecognized)
		{
			ButtonIndex = buttonIndex;
		}

		protected internal override bool Cancel(Gesture sender)
		{
			if (state == State.Idle) {
				return sender == null || !(sender is TapGesture);
			}
			if (sender == null || sender is TapGesture) {
				return false;
			}
			if (state == State.WaitSecondPress) {
				RaiseCanceled();
			}
			return true;
		}

		protected internal override void Update(float delta)
		{
			timeSinceFirstPress += delta;

			if (state != State.Idle && timeSinceFirstPress > MaxDelayBetweenClicks) {
				state = State.Idle;
				if (state == State.WaitSecondPress) {
					RaiseCanceled();
				}
				RaiseEnded();
				return;
			}

			if (state == State.Idle && Input.WasMousePressed(ButtonIndex)) {
				if (WasRecognized()) {
					// Prevent a spurious call on the same frame as the recognition.
					return;
				}
				timeSinceFirstPress = 0.0f;
				firstPressPosition = Input.MousePosition;
				state = State.FirstPress;
			}

			if (state == State.FirstPress && !Input.IsMousePressed(ButtonIndex)) {
				state = State.WaitSecondPress;
				RaiseBegan();
			}

			if (state == State.WaitSecondPress && Input.WasMousePressed(ButtonIndex)) {
				state = State.Idle;
				if (Input.GetNumTouches() == 1 && IsCloseToFirstPressPosition(Input.MousePosition)) {
					RaiseRecognized();
				} else {
					RaiseCanceled();
				}
				RaiseEnded();
			}

			bool IsCloseToFirstPressPosition(Vector2 mousePosition)
			{
				return new Rectangle(
					firstPressPosition - DoubleClickThreshold,
					firstPressPosition + DoubleClickThreshold
				).Contains(mousePosition);
			}
		}
	}
}
