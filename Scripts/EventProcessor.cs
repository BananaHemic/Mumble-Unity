#define EVENT_DEBUG
using UnityEngine;
using System;
using System.Collections.Generic;

/*
 * Event processor to ensure events are executed from the main thread.
 * http://stackoverflow.com/questions/22513881/unity3d-how-to-process-events-in-the-correct-thread
 */


public class EventProcessor : MonoBehaviour
{
    public static EventProcessor Instance { get; private set; }

	private readonly object _queueLock = new object();
	private readonly Queue<Action> _queuedEvents = new Queue<Action>();
	private readonly Queue<Action> _executingEvents = new Queue<Action>();
#if EVENT_DEBUG
    private bool _wasJustPaused = false;
#endif

    private void Awake()
    {
        if(Instance != null)
        {
            Debug.LogError("Destroying EventProcessor!");
            Destroy(Instance);
        }
        Instance = this;
    }
#if EVENT_DEBUG
    private void OnApplicationPause(bool pause)
    {
        if (pause)
            _wasJustPaused = true;
    }
#endif
    public void QueueEvent(Action action)
	{
		lock (_queueLock)
		{
			_queuedEvents.Enqueue(action);
#if EVENT_DEBUG
            Debug.Log("Queue'd #" + _queuedEvents.Count);
#endif
		}
	}
	private void MoveQueuedEventsToExecuting()
	{
		lock (_queueLock)
		{
			while (_queuedEvents.Count > 0)
			{
#if EVENT_DEBUG
                if (_wasJustPaused)
                    Debug.Log("MoveQ count: " + _queuedEvents.Count);
#endif

                Action e = _queuedEvents.Dequeue();
#if EVENT_DEBUG
                if (_wasJustPaused)
                    Debug.Log("a");
#endif
				_executingEvents.Enqueue(e);
#if EVENT_DEBUG
                if (_wasJustPaused)
                    Debug.Log("b");
#endif
			}
		}
	}
	void Update()
	{
#if EVENT_DEBUG
        if (_wasJustPaused)
            Debug.Log("Event processor start update");
#endif
		MoveQueuedEventsToExecuting();

		while (_executingEvents.Count > 0)
		{
#if EVENT_DEBUG
            if (_wasJustPaused)
                Debug.Log("Execute count: " + _executingEvents.Count);
#endif
            Action e = _executingEvents.Dequeue();
#if EVENT_DEBUG
            if (_wasJustPaused)
                Debug.Log("A");
#endif
			e();
		}
	}
}
