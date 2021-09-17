using System;
using System.Threading.Tasks;

namespace ModOptExtensions {
    static class ModOptExtensions {
        public static Task<TOutput> Then<TInput, TOutput>(this Task<TInput> task, Func<TInput, TOutput> func) {
            return task.ContinueWith((input) => func(input.Result));
        }

        public static Task Then(this Task task, Action<Task> func) { return task.ContinueWith(func); }

        public static Task Then<TInput>(this Task<TInput> task, Action<TInput> func) {
            return task.ContinueWith((input) => func(input.Result));
        }
    }
}
