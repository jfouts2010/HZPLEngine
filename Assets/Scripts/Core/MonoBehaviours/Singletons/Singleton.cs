using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : Component
{
    private static object _lock = new object();
    public static bool applicationIsQuitting = false;
    private static T instance;
    public static T Instance
    {
        get
        {
            if (applicationIsQuitting)
            {
                return null;
            }
            lock (_lock)
            {
                if (instance == null)
                {
                    instance = (T)FindObjectOfType(typeof(T));
                    if (instance == null)
                    {
                        SetupInstance();
                    }
                }
            }
            return instance;
        }
    }
    public virtual void Awake()
    {
        Application.quitting += IsQuitting;
    }

    private void IsQuitting()
    {
        applicationIsQuitting = true;
    }
    private static void SetupInstance()
    {
        instance = (T)FindObjectOfType(typeof(T));
        if (instance == null)
        {
            GameObject gameObj = new GameObject();
            gameObj.name = typeof(T).Name;
            instance = gameObj.AddComponent<T>();
            
            DontDestroyOnLoad(gameObj);
        }
    }
}
