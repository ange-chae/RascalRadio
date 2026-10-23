using Godot;

public partial class WarningTriangle : Control
{
	[Export] public Color LitColor { get; set; } = new Color(1f, 0.35f, 0.15f);
	[Export] public Color OffColor { get; set; } = new Color(0.25f, 0.25f, 0.25f);

	private static readonly Color Mark = new Color(0.12f, 0.12f, 0.12f);

	private bool _lit;
	private double _time;

	public bool Lit
	{
		get => _lit;
		set
		{
			if (_lit == value) return;
			_lit = value;
			_time = 0.0;
			QueueRedraw();
		}
	}

	public override void _Process(double delta)
	{
		if (!_lit) return;

		_time += delta;
		QueueRedraw();
	}

	public override void _Draw()
	{
		Vector2 size = Size;
		Vector2 top = new Vector2(size.X * 0.5f, size.Y * 0.08f);
		Vector2 left = new Vector2(size.X * 0.06f, size.Y * 0.92f);
		Vector2 right = new Vector2(size.X * 0.94f, size.Y * 0.92f);

		if (_lit)
		{
			float t = (float)((_time % 1.2) / 1.2);
			Vector2 center = (top + left + right) / 3f;
			float scale = 1f + t * 0.7f;
			Color ring = new Color(LitColor.R, LitColor.G, LitColor.B, 1f - t);

			Vector2[] outline =
			{
				center + (top - center) * scale,
				center + (left - center) * scale,
				center + (right - center) * scale,
				center + (top - center) * scale,
			};
			DrawPolyline(outline, ring, 2f);
		}

		float pulse = _lit ? 0.8f + 0.2f * Mathf.Sin((float)_time * 7f) : 1f;
		Color body = _lit ? new Color(LitColor.R, LitColor.G, LitColor.B, pulse) : OffColor;
		DrawColoredPolygon(new[] { top, left, right }, body);

		float stroke = Mathf.Max(2f, size.X * 0.08f);
		DrawLine(new Vector2(size.X * 0.5f, size.Y * 0.38f), new Vector2(size.X * 0.5f, size.Y * 0.64f), Mark, stroke);
		DrawCircle(new Vector2(size.X * 0.5f, size.Y * 0.78f), Mathf.Max(1.5f, size.X * 0.05f), Mark);
	}
}
