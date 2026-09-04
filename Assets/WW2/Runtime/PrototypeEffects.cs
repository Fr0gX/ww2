using UnityEngine;
using WW2.Core.Model;

namespace WW2.Runtime
{
    public sealed class SelectionPulseEffect : MonoBehaviour
    {
        private Vector3 _baseScale;
        private float _phase;

        public void Initialize(float phase)
        {
            _baseScale = transform.localScale;
            _phase = phase;
        }

        private void Update()
        {
            var pulse = 1f + Mathf.Sin(Time.unscaledTime * 4.5f + _phase) * 0.10f;
            transform.localScale = _baseScale * pulse;
        }
    }

    public sealed class WorldActionEffect : MonoBehaviour
    {
        private Vector3 _start;
        private Vector3 _end;
        private Vector3 _baseScale;
        private float _duration;
        private float _arc;
        private float _startedAt;

        public void Initialize(Vector3 start, Vector3 end, float duration, float arc)
        {
            _start = start;
            _end = end;
            _duration = Mathf.Max(0.1f, duration);
            _arc = arc;
            _baseScale = transform.localScale;
            _startedAt = Time.unscaledTime;
            transform.position = start;
        }

        private void Update()
        {
            var t = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
            transform.position = Vector3.Lerp(_start, _end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * _arc);
            var shrink = t < 0.8f ? 1f : 1f - (t - 0.8f) / 0.2f;
            transform.localScale = _baseScale * Mathf.Max(0.05f, shrink);
            if (t >= 1f) Destroy(gameObject);
        }
    }

    public sealed class WorldPulseEffect : MonoBehaviour
    {
        private Vector3 _baseScale;
        private float _duration;
        private float _startedAt;

        public void Initialize(float duration)
        {
            _baseScale = transform.localScale;
            _duration = Mathf.Max(0.1f, duration);
            _startedAt = Time.unscaledTime;
        }

        private void Update()
        {
            var t = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
            var scale = Mathf.Lerp(0.35f, 1.35f, t);
            transform.localScale = _baseScale * scale;
            if (t >= 1f) Destroy(gameObject);
        }
    }

    public sealed class FloatingDamageEffect : MonoBehaviour
    {
        private float _duration;
        private float _startedAt;
        private Vector3 _origin;
        private TextMesh[] _texts;
        private float _drift;

        public void Initialize(float duration)
        {
            _duration = Mathf.Max(0.2f, duration);
            _startedAt = Time.unscaledTime;
            _origin = transform.position;
            _texts = GetComponentsInChildren<TextMesh>();
            _drift = (GetInstanceID() & 1) == 0 ? 0.22f : -0.22f;
        }

        private void Update()
        {
            var t = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
            var rise = 1f - Mathf.Pow(1f - t, 2f);
            transform.position = _origin + Vector3.up * (rise * 1.45f) + Vector3.right * (_drift * t);
            var scale = t < 0.20f
                ? Mathf.Lerp(0.30f, 1.32f, 1f - Mathf.Pow(1f - t / 0.20f, 3f))
                : Mathf.Lerp(1.32f, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.20f) / 0.35f));
            transform.localScale = Vector3.one * scale;
            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;
            var alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.58f) / 0.42f));
            if (_texts != null)
            {
                foreach (var text in _texts)
                {
                    var color = text.color;
                    color.a = alpha;
                    text.color = color;
                }
            }
            if (t >= 1f) Destroy(gameObject);
        }
    }

    public sealed class UnitPathMoveEffect : MonoBehaviour
    {
        private Vector3[] _points;
        private float _secondsPerCell;
        private float _startedAt;
        private Vector3 _baseScale;

        public void Initialize(Vector3[] points, float secondsPerCell)
        {
            _points = points;
            _secondsPerCell = Mathf.Max(0.06f, secondsPerCell);
            _startedAt = Time.unscaledTime;
            _baseScale = transform.localScale;
            transform.position = points[0];
        }

        private void Update()
        {
            if (_points == null || _points.Length < 2)
            {
                Destroy(this);
                return;
            }
            var total = Mathf.Min(0.95f, (_points.Length - 1) * _secondsPerCell);
            var t = Mathf.Clamp01((Time.unscaledTime - _startedAt) / total);
            var eased = t * t * (3f - 2f * t);
            var progress = eased * (_points.Length - 1);
            var segment = Mathf.Min(_points.Length - 2, Mathf.FloorToInt(progress));
            var local = progress - segment;
            var p0 = _points[Mathf.Max(0, segment - 1)];
            var p1 = _points[segment];
            var p2 = _points[segment + 1];
            var p3 = _points[Mathf.Min(_points.Length - 1, segment + 2)];
            var local2 = local * local;
            var local3 = local2 * local;
            var position = 0.5f * ((2f * p1) + (-p0 + p2) * local +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * local2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * local3);
            transform.position = position + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.075f);
            var motion = Mathf.Sin(t * Mathf.PI);
            transform.localScale = new Vector3(_baseScale.x * (1f + motion * 0.025f),
                _baseScale.y * (1f - motion * 0.035f), _baseScale.z * (1f + motion * 0.025f));
            var direction = p2 - p1;
            if (direction.sqrMagnitude > 0.001f)
            {
                var desired = Quaternion.LookRotation(direction.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, desired, Time.unscaledDeltaTime * 11f);
            }
            if (t < 1f) return;
            transform.position = _points[_points.Length - 1];
            transform.rotation = Quaternion.identity;
            transform.localScale = _baseScale;
            var trail = GetComponent<TrailRenderer>();
            if (trail != null) trail.emitting = false;
            Destroy(this);
            if (trail != null) Destroy(trail, trail.time);
        }

        public void CompleteImmediately()
        {
            if (_points != null && _points.Length > 0) transform.position = _points[_points.Length - 1];
            transform.rotation = Quaternion.identity;
            transform.localScale = _baseScale;
            var trail = GetComponent<TrailRenderer>();
            if (trail != null) trail.emitting = false;
            enabled = false;
            Destroy(this);
            if (trail != null) Destroy(trail);
        }
    }

    public sealed class UnitLungeEffect : MonoBehaviour
    {
        private Vector3 _origin;
        private Vector3 _target;
        private Vector3 _baseScale;
        private float _duration;
        private float _startedAt;

        public void Initialize(Vector3 target, float duration)
        {
            _origin = transform.position;
            _target = new Vector3(target.x, _origin.y, target.z);
            _baseScale = transform.localScale;
            _duration = Mathf.Max(0.18f, duration);
            _startedAt = Time.unscaledTime;
        }

        private void Update()
        {
            var t = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
            var strike = t < 0.42f ? Mathf.SmoothStep(0f, 1f, t / 0.42f) :
                1f - Mathf.SmoothStep(0f, 1f, (t - 0.42f) / 0.58f);
            transform.position = Vector3.Lerp(_origin, _target, strike * 0.23f) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.08f;
            transform.localScale = new Vector3(_baseScale.x * (1f + strike * 0.12f),
                _baseScale.y * (1f - strike * 0.12f), _baseScale.z * (1f + strike * 0.12f));
            if (t < 1f) return;
            transform.position = _origin;
            transform.localScale = _baseScale;
            Destroy(this);
        }
    }

    public sealed class UnitWeaponRecoilEffect : MonoBehaviour
    {
        private Vector3 _origin;
        private Vector3 _baseScale;
        private Quaternion _originRotation;
        private Vector3 _direction;
        private UnitType _type;
        private float _startedAt;
        private float _duration;

        public void Initialize(UnitType type, Vector3 target, float duration)
        {
            _type = type;
            _origin = transform.position;
            _baseScale = transform.localScale;
            _originRotation = transform.rotation;
            _direction = target - _origin;
            _direction.y = 0f;
            if (_direction.sqrMagnitude < 0.001f) _direction = Vector3.forward;
            _direction.Normalize();
            _duration = Mathf.Max(0.18f, duration);
            _startedAt = Time.unscaledTime;
        }

        private void Update()
        {
            var t = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
            var kick = Mathf.Sin(t * Mathf.PI);
            var forward = _type == UnitType.MainInfantry ? 0.13f : _type == UnitType.Medic ? 0.08f :
                _type == UnitType.LightArmor ? -0.10f : -0.16f;
            var squash = _type == UnitType.LightArtillery ? 0.08f : 0.035f;
            transform.position = _origin + _direction * (forward * kick);
            transform.rotation = Quaternion.Slerp(_originRotation,
                Quaternion.LookRotation(_direction, Vector3.up), Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 4f)));
            transform.localScale = new Vector3(_baseScale.x * (1f + kick * squash),
                _baseScale.y * (1f - kick * squash), _baseScale.z * (1f + kick * squash));
            if (t < 1f) return;
            transform.position = _origin;
            transform.rotation = _originRotation;
            transform.localScale = _baseScale;
            Destroy(this);
        }
    }

    public sealed class FxBurstEffect : MonoBehaviour
    {
        private Renderer _renderer;
        private Material _material;
        private Vector3 _baseScale;
        private Vector3 _velocity;
        private float _startedAt;
        private float _duration;
        private float _growth;

        public void Initialize(float duration, float growth, Vector3 velocity)
        {
            _renderer = GetComponent<Renderer>();
            _material = _renderer == null ? null : _renderer.material;
            _baseScale = transform.localScale;
            _velocity = velocity;
            _duration = Mathf.Max(0.08f, duration);
            _growth = growth;
            _startedAt = Time.unscaledTime;
        }

        private void LateUpdate()
        {
            var t = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
            transform.position += _velocity * Time.unscaledDeltaTime;
            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;
            var punch = 1f - Mathf.Pow(1f - Mathf.Min(1f, t * 5f), 3f);
            transform.localScale = _baseScale * Mathf.Lerp(0.30f, _growth, punch);
            if (_material != null)
            {
                var color = _material.color;
                color.a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.28f) / 0.72f));
                _material.color = color;
            }
            if (t < 1f) return;
            if (_material != null) Destroy(_material);
            Destroy(gameObject);
        }
    }

    public sealed class ImpactFragmentEffect : MonoBehaviour
    {
        private Vector3 _velocity;
        private float _duration;
        private float _startedAt;
        private Vector3 _baseScale;

        public void Initialize(Vector3 direction, float duration)
        {
            _velocity = direction.normalized * 2.2f;
            _duration = Mathf.Max(0.18f, duration);
            _startedAt = Time.unscaledTime;
            _baseScale = transform.localScale;
        }

        private void Update()
        {
            var t = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
            transform.position += _velocity * Time.unscaledDeltaTime;
            transform.rotation *= Quaternion.Euler(0f, 420f * Time.unscaledDeltaTime, 260f * Time.unscaledDeltaTime);
            transform.localScale = _baseScale * (1f - t);
            if (t >= 1f) Destroy(gameObject);
        }
    }

    public sealed class UnitHitReactEffect : MonoBehaviour
    {
        private Vector3 _origin;
        private Vector3 _baseScale;
        private Vector3 _direction;
        private float _startedAt;
        private float _duration;

        public void Initialize(Vector3 direction, float duration)
        {
            _origin = transform.position;
            _baseScale = transform.localScale;
            _direction = direction.sqrMagnitude < 0.001f ? Vector3.back : direction.normalized;
            _startedAt = Time.unscaledTime;
            _duration = Mathf.Max(0.16f, duration);
        }

        private void Update()
        {
            var t = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
            var kick = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.35f);
            var shake = Mathf.Sin(t * Mathf.PI * 7f) * (1f - t) * 0.045f;
            transform.position = _origin + _direction * kick * 0.18f + Vector3.right * shake;
            transform.localScale = new Vector3(_baseScale.x * (1f + kick * 0.10f),
                _baseScale.y * (1f - kick * 0.13f), _baseScale.z * (1f + kick * 0.10f));
            if (t < 1f) return;
            transform.position = _origin;
            transform.localScale = _baseScale;
            Destroy(this);
        }
    }

    public sealed class UnitDeathEffect : MonoBehaviour
    {
        private Vector3 _origin;
        private Vector3 _baseScale;
        private Quaternion _baseRotation;
        private float _startedAt;
        private float _duration;

        private UnitType _type;
        private Vector3 _fallDirection;

        public void Initialize(float duration, UnitType type, Vector3 fallDirection)
        {
            _origin = transform.position;
            _baseScale = transform.localScale;
            _baseRotation = transform.rotation;
            _startedAt = Time.unscaledTime;
            _duration = Mathf.Max(0.38f, duration);
            _type = type;
            _fallDirection = fallDirection.sqrMagnitude < 0.001f ? Vector3.right : fallDirection.normalized;
        }

        private void Update()
        {
            var t = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
            var hit = Mathf.Clamp01(t / 0.20f);
            var vanish = Mathf.Clamp01((t - 0.52f) / 0.48f);
            var shakeStrength = _type == UnitType.LightArmor ? 0.11f : 0.055f;
            var shake = Mathf.Sin(t * Mathf.PI * 12f) * (1f - t) * shakeStrength;
            var personnel = _type == UnitType.MainInfantry || _type == UnitType.Medic;
            var artillery = _type == UnitType.LightArtillery;
            transform.position = _origin + Vector3.right * shake + _fallDirection *
                (personnel ? hit * 0.18f : artillery ? hit * 0.10f : 0f) + Vector3.down * vanish * 0.22f;
            var fallAngle = personnel ? 78f : artillery ? 42f : 12f;
            transform.rotation = _baseRotation * Quaternion.Euler(hit * fallAngle, artillery ? hit * 32f : t * 8f,
                personnel ? hit * 24f : artillery ? -hit * 26f : 0f);
            transform.localScale = _baseScale * Mathf.Max(0.02f, 1f - vanish * vanish);
            if (t >= 1f) Destroy(gameObject);
        }
    }

    public sealed class CameraImpulseEffect : MonoBehaviour
    {
        private float _startedAt;
        private float _duration;
        private float _strength;
        private Vector3 _lastOffset;

        public void Initialize(float strength, float duration)
        {
            _startedAt = Time.unscaledTime;
            _duration = Mathf.Max(0.10f, duration);
            _strength = strength;
        }

        private void LateUpdate()
        {
            transform.position -= _lastOffset;
            var t = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
            var fade = (1f - t) * (1f - t);
            _lastOffset = new Vector3(Mathf.Sin(t * 47f), Mathf.Sin(t * 61f) * 0.35f,
                Mathf.Cos(t * 53f)) * (_strength * fade);
            transform.position += _lastOffset;
            if (t < 1f) return;
            transform.position -= _lastOffset;
            Destroy(this);
        }
    }
}
