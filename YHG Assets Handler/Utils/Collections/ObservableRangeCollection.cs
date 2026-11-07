using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace YHG_Assets_Handler.Utils.Collections
{
    public class ObservableRangeCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotification = false;

        public ObservableRangeCollection() : base() { }
        public ObservableRangeCollection(IEnumerable<T> collection) : base(collection) { }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotification)
                base.OnCollectionChanged(e);
        }

        public void AddRange(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            var list = (List<T>)Items;
            int countBefore = list.Count;

            _suppressNotification = true;
            try
            {
                list.AddRange(collection);
            }
            finally
            {
                _suppressNotification = false;
            }

            if (list.Count != countBefore)
            {
                RaiseBatchReset();
            }
        }

        public void RemoveRange(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            var toRemove = new HashSet<T>(collection);
            if (toRemove.Count == 0 || Items.Count == 0) return;

            var list = (List<T>)Items;
            int countBefore = list.Count;

            _suppressNotification = true;
            try
            {
                list.RemoveAll(toRemove.Contains);
            }
            finally
            {
                _suppressNotification = false;
            }

            if (list.Count != countBefore)
            {
                RaiseBatchReset();
            }
        }

        private void RaiseBatchReset()
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void ReplaceAll(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            var list = (List<T>)Items;

            _suppressNotification = true;
            try
            {
                list.Clear();
                list.AddRange(collection);
            }
            finally
            {
                _suppressNotification = false;
            }

            RaiseBatchReset();
        }
    }
}