using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instant;

    public static T Instant
    {
        get
        {
            // Neu da co instance thi tra ve luon
            if (_instant != null)
                return _instant;

            // Thu tim instance co san trong scene
            _instant = FindObjectOfType<T>();

            // Khong tim thay -> tao moi GameObject chua singleton
            if (_instant == null)
            {
                var obj = new GameObject("Singleton_" + typeof(T).Name);
                _instant = obj.AddComponent<T>();

                // De object nay ton tai qua cac scene (neu can)
                DontDestroyOnLoad(obj);
            }

            return _instant;
        }
                            }

    protected virtual void Awake()
    {
        // Neu da co instance khac va khong phai chinh object nay -> huy duplicate
        if (_instant != null && _instant != this as T)
        {
            Debug.LogWarning($"[Singleton<{typeof(T).Name}>] Instance da ton tai ({_instant.gameObject.name}), huy {gameObject.name}");
            Destroy(gameObject);
            return;
        }

        _instant = this as T;

        // De object nay ton tai qua cac scene
        DontDestroyOnLoad(gameObject);
    }

    protected virtual void OnDestroy()
    {
        // Chi clear static neu day chinh la instance
        if (_instant == this as T)
        {
            _instant = null;
        }
    }
}
