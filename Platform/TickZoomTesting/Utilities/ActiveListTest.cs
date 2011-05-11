using System;
using System.Threading;
using NUnit.Framework;
using TickZoom.Api;
using System.IO;

namespace TickZoom.Utilities
{
    [TestFixture]
    public class ActiveListTest
    {
        private ActiveList<int> list;
        private int nextValue = 0;
        private volatile bool stopThread = false;
        private Exception threadException;
        private long addCounter;
        private long removeCounter;
        private long readCounter;
        private long addFailureCounter;
        private long readFailureCounter;
        private Random random = new Random();

        [SetUp]
        public void Setup()
        {
            stopThread = false;
            list = new ActiveList<int>();
            nextValue = 0;
            addCounter = 0;
            removeCounter = 0;
            readCounter = 0;
            addFailureCounter = 0;
            readFailureCounter = 0;
        }

        [Test]
        public void TestAddFirst()
        {
            AddToList();
            Assert.AreEqual(1, list.Count);
            AddToList();
            Assert.AreEqual(2, list.Count);
            AddToList();
            Assert.AreEqual(3, list.Count);
            AddToList();
            Assert.AreEqual(4, list.Count);
            AddToList();
            Assert.AreEqual(5, list.Count);
        }

        [Test]
        public void MemoryStreamExperiment()
        {
            var memory = new MemoryStream();
            memory.SetLength(181);
            memory.Position = 0;
            var pos = memory.Position;
        }

        [Test]
        public void TestAddLast()
        {
            list.AddLast(3);
            Assert.AreEqual(1, list.Count);
            list.AddLast(2);
            Assert.AreEqual(2, list.Count);
            list.AddLast(5);
            Assert.AreEqual(3, list.Count);
            list.AddLast(4);
            Assert.AreEqual(4, list.Count);
            list.AddLast(1);
            Assert.AreEqual(5, list.Count);
            var node = list.First;
            Assert.AreEqual(3, node.Value);
            node = node.Next;
            Assert.AreEqual(2, node.Value);
            node = node.Next;
            Assert.AreEqual(5, node.Value);
            node = node.Next;
            Assert.AreEqual(4, node.Value);
            node = node.Next;
            Assert.AreEqual(1, node.Value);
        }

        [Test]
        public void TestSortFirst()
        {
            list.SortFirst(3, (x, y) => x - y);
            Assert.AreEqual(1,list.Count);
            list.SortFirst(2, (x, y) => x - y);
            Assert.AreEqual(2, list.Count);
            list.SortFirst(5, (x, y) => x - y);
            Assert.AreEqual(3, list.Count);
            list.SortFirst(4, (x, y) => x - y);
            Assert.AreEqual(4, list.Count);
            list.SortFirst(1, (x, y) => x - y);
            Assert.AreEqual(5, list.Count);
            var node = list.First;
            Assert.AreEqual(1,node.Value);
            node = node.Next;
            Assert.AreEqual(2, node.Value);
            node = node.Next;
            Assert.AreEqual(3, node.Value);
            node = node.Next;
            Assert.AreEqual(4, node.Value);
            node = node.Next;
            Assert.AreEqual(5, node.Value);
        }

        [Test]
        public void TestRemoveWhileReading()
        {
            for (var i = 0; i < 50; i++)
            {
                list.AddLast(Interlocked.Increment(ref nextValue));
            }
            var next = list.First;
            var item = next.Value;
            for (var current = next; current != null; current = next)
            {
                next = current.Next;
                item = current.Value;
                if( item == 10)
                {
                    list.Remove(next);
                }
                if( item == 35)
                {
                    break;
                }
            }
            Assert.AreEqual(35,item,"found item");
        }

        [Test]
        public void TestRemovingWhilePrevious()
        {
            for (var i = 0; i < 50; i++)
            {
                list.AddLast(Interlocked.Increment(ref nextValue));
            }
            var previous = list.Last;
            var item = previous.Value;
            for (var current = previous; current != null; current = previous)
            {
                previous = current.Previous;
                item = current.Value;
                if (item == 35)
                {
                    list.Remove(previous);
                }
                if (item == 10)
                {
                    break;
                }
            }
            Assert.AreEqual(10, item, "found item");
        }

        [Test]
        public void TestRemove()
        {
            for (var i = 0; i < 10; i++)
            {
                list.AddLast(i);
            }
            Assert.AreEqual(0, list.First.Value);
            Assert.AreEqual(9, list.Last.Value);
            list.RemoveFirst();
            Assert.AreEqual(1, list.First.Value);
            Assert.AreEqual(9, list.Last.Value);
            list.RemoveLast();
            Assert.AreEqual(1, list.First.Value);
            Assert.AreEqual(8, list.Last.Value);
            list.Remove(4);
            list.Remove(7);
            list.Remove(8);
            list.Remove(1);
            var node = list.First;
            Assert.AreEqual(2, node.Value);
            node = node.Next;
            Assert.AreEqual(3, node.Value);
            node = node.Next;
            Assert.AreEqual(5, node.Value);
            node = node.Next;
            Assert.AreEqual(6, node.Value);
            node = node.Next;
            Assert.AreEqual(null, node);
        }

        [Test]
        public void TestFind()
        {
            for (var i = 0; i < 10; i++)
            {
                list.AddLast(i);
            }
            var node = list.Find(5);
            Assert.AreEqual(5, node.Value);
            node = list.Find(20);
            Assert.AreEqual(null, node);
            node = list.Find(-5);
            Assert.AreEqual(null, node);
        }

        [Test]
        public void TestRemoving()
        {
            for (var i = 0; i < 5; i++ )
            {
                list.AddLast(Interlocked.Increment(ref nextValue));
            }
            RemoveFromList();
            Assert.AreEqual(4, list.Count);
            RemoveFromList();
            Assert.AreEqual(3, list.Count);
            RemoveFromList();
            Assert.AreEqual(2, list.Count);
            RemoveFromList();
            Assert.AreEqual(1, list.Count);
            RemoveFromList();
            Assert.AreEqual(0, list.Count);
            RemoveFromList();
            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void TestAddThread()
        {
            for (var i = 0; i < 100; i++)
            {
                list.AddLast(Interlocked.Increment(ref nextValue));
            }
            var addThread = new Thread(AddToListLoop);
            addThread.Start();
            Thread.Sleep(5000);
            stopThread = true;
            addThread.Join();
            if (threadException != null)
            {
                throw new Exception("Thread failed: ", threadException);
            }
            Assert.Greater(addCounter,5000, "add counter");
            Console.Out.WriteLine("addCounter " + addCounter);
        }

        [Test]
        public void TestReaderThread()
        {
            for (var i = 0; i < 100; i++)
            {
                list.AddLast(Interlocked.Increment(ref nextValue));
            }
            var readThread = new Thread(ReadFromListLoop);
            readThread.Start();
            Thread.Sleep(5000);
            stopThread = true;
            readThread.Join();
            if (threadException != null)
            {
                throw new Exception("Thread failed: ", threadException);
            }
            Assert.Greater(readCounter,5000, "remove counter");
            Console.Out.WriteLine("readCounter " + readCounter);
        }

        [Test]
        public void TestRemoveThread()
        {
            for (var i = 0; i < 100000; i++)
            {
                list.AddLast(Interlocked.Increment(ref nextValue));
            }
            var removeThread = new Thread(RemoveFromListLoop);
            removeThread.Start();
            Thread.Sleep(5000);
            stopThread = true;
            removeThread.Join();
            if (threadException != null)
            {
                throw new Exception("Thread failed: ", threadException);
            }
            Assert.Greater(removeCounter,4000, "remove counter");
            Console.Out.WriteLine("removeCounter " + removeCounter);
        }

        [Test]
        public void TestReaderWriterSafety()
        {
            for (var i = 0; i < 100; i++)
            {
                list.AddLast(Interlocked.Increment(ref nextValue));
            }
            var addThread = new Thread(AddToListLoop);
            addThread.Start();
            var readThread = new Thread(ReadFromListLoop);
            readThread.Start();
            var readThread2 = new Thread(ReadFromListLoop);
            readThread2.Start();
            Thread.Sleep(5000);
            stopThread = true;
            addThread.Join();
            readThread.Join();
            readThread2.Join();
            if (threadException != null)
            {
                throw new Exception("Thread failed: ", threadException);
            }
            Assert.Less(readFailureCounter,5,"read failure");
            Assert.Greater(readCounter,1000000, "read counter");
            Assert.Greater(addCounter,4000, "add counter");
            Console.Out.WriteLine("readFailure " + readFailureCounter);
            Console.Out.WriteLine("readCounter " + readCounter);
            Console.Out.WriteLine("addCounter " + addCounter);
        }

        [Test]
        public void Test2WritersSafety()
        {
            for (var i = 0; i < 100; i++)
            {
                list.AddLast(Interlocked.Increment(ref nextValue));
            }
            var addThread = new Thread(AddToListLoop);
            addThread.Start();
            var removeThread = new Thread(RemoveFromListLoop);
            removeThread.Start();
            Thread.Sleep(5000);
            stopThread = true;
            addThread.Join();
            removeThread.Join();
            if( threadException != null )
            {
                throw new Exception("Thread failed: ", threadException);
            }
            Assert.Less(addFailureCounter, 100, "failure counter");
            Assert.Greater(addCounter,2000, "add counter");
            Assert.Greater(removeCounter,2000, "remove counter");
            Console.Out.WriteLine("addFailure " + addFailureCounter);
            Console.Out.WriteLine("removeCounter " + removeCounter);
            Console.Out.WriteLine("addCounter " + addCounter);
        }

        public void AddToListLoop()
        {
            try
            {
                while (!stopThread)
                {
                    AddToList();
                }
            }
            catch( Exception ex)
            {
                Interlocked.Exchange(ref threadException, ex);
            }
        }

        public void ReadFromListLoop()
        {
            try
            {
                while (!stopThread)
                {
                    ReadFromList2();
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref threadException, ex);
            }
        }



        public void ReadFromList()
        {
            var random = new Random();
            if (list.Count == 0)
            {
                return;
            }
            else
            {
                var total = 0L;
                var index = random.Next(list.Count);
                if( list.Count != 0 )
                {
                    var current = list.First;
                    var i = 0;
                    do
                    {
                        current = current.Next;
                        i++;
                        if( i>index)
                        {
                            break;
                        }
                    } while (current != null);
                    if (current != null)
                    {
                        total += current.Value;
                        Interlocked.Increment(ref readCounter);
                    }
                    else
                    {
                        Interlocked.Increment(ref readFailureCounter);
                    }
                }
            }
        }

        public void ReadFromList2()
        {
            if (list.Count == 0)
            {
                return;
            }
            else
            {
                var total = 0L;
                for (var node = list.First; node != null; node = node.Next)
                {
                    Interlocked.Increment(ref readCounter);
                }
            }
        }

        public void AddToList()
        {
            var index = random.Next(list.Count);
            list.SortFirst(index, (x, y) => x - y );
            Interlocked.Increment(ref addCounter);
        }

        public void RemoveFromListLoop()
        {
            try {
                while( !stopThread)
                {
                    RemoveFromList();
                }
            }
            catch( Exception ex)
            {
                Interlocked.Exchange(ref threadException, ex);
            }
        }

        public void RemoveFromList()
        {
            var random = new Random();

            if( list.Count == 0)
            {
                return;
            } else
            {
                var index = random.Next(list.Count-1);
                var current = list.First;
                for (int i = 0; i < index && current != null; i++, current = current.Next) ;
                if( current != null)
                {
                    list.Remove(current);
                    Interlocked.Increment(ref removeCounter);
                }
            }
        }
    }
}