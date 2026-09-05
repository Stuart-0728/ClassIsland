using ClassIsland.Shared.Models.Notification;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
var normal = new NotificationPriority(0, 1, false, false);
var urgent = new NotificationPriority(-200, 2, true, false);
var intercom = new NotificationPriority(-50, 3, true, false);
Check(urgent.CompareTo(normal) < 0 && normal.CompareTo(urgent) > 0, "urgent preempts normal symmetrically");
Check(urgent.CompareTo(urgent) == 0, "priority comparison is reflexive");
Check(urgent.CompareTo(intercom) < 0, "emergency precedes ordinary intercom");
Check(new NotificationPriority(-200, 2, true, false).CompareTo(new NotificationPriority(-200, 3, true, false)) < 0, "same-priority notifications remain FIFO");
var queue = new PriorityQueue<string, NotificationPriority>();
queue.Enqueue("normal", normal);
queue.Enqueue("intercom", intercom);
queue.Enqueue("emergency", urgent);
Check(queue.Dequeue() == "emergency" && queue.Dequeue() == "intercom" && queue.Dequeue() == "normal", "actual queue preserves priority order");
