using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud;

internal sealed class TrafficMotion
{
    public const int You = TelemetryFrame.MaxCars;
    private const double MoveSettle = 0.14, FadeSettle = 0.12, LongPause = 1;

    private readonly float[] _pos = new float[You + 1], _target = new float[You + 1], _alpha = new float[You + 1];
    private readonly bool[] _known = new bool[You + 1], _live = new bool[You + 1];
    private double _last = double.NaN;
    private float _move = 1, _fade = 1;

    public bool Fresh { get; private set; } = true;

    public void Begin(double now, bool still)
    {
        double dt = now - _last;
        Fresh = still || double.IsNaN(dt) || dt <= 0 || dt > LongPause;
        _last = now;
        _move = Fresh ? 1 : (float)(1 - Math.Exp(-dt / MoveSettle));
        _fade = Fresh ? 1 : (float)(1 - Math.Exp(-dt / FadeSettle));
        Array.Clear(_live);
    }

    public void Show(int key, float target)
    {
        if (!_known[key])
        {
            _known[key] = true;
            _pos[key] = target;
            _alpha[key] = 0;
        }
        _live[key] = true;
        _target[key] = target;
    }

    public void End()
    {
        for (int i = 0; i <= You; i++)
        {
            if (!_known[i]) continue;
            _pos[i] += (_target[i] - _pos[i]) * _move;
            _alpha[i] += ((_live[i] ? 1 : 0) - _alpha[i]) * _fade;
            if (!_live[i] && (Fresh || _alpha[i] < 0.02f)) _known[i] = false;
        }
    }

    public bool Visible(int key, out float pos, out float alpha)
    {
        pos = _pos[key];
        alpha = _alpha[key];
        return _known[key];
    }

    public bool Live(int key) => _live[key];

    public float Toward(float value, float target) => value + (target - value) * _fade;
}
