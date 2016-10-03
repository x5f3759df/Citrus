﻿using System;
using Lime;
using Tangerine.Core;
using Tangerine.UI;

namespace Tangerine
{
	public class DefaultLayoutCommand : Command
	{
		public override string Text => "Default Layout";

		public override void Execute()
		{
			DockManager.Instance.ImportState(TangerineApp.Instance.DockManagerInitialState, resizeMainWindow: false);
		}
	}
}
