﻿using Medallion.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Medallion.Shell.Tests
{
    [TestClass]
    public class GeneralTest
    {
        [TestMethod]
        public void TestGrep()
        {
            var command = Shell.Default.Run("SampleCommand", "grep", "a+");
            command.StandardInput.WriteLine("hi");
            command.StandardInput.WriteLine("aa");
            command.StandardInput.Close();
            command.StandardOutput.ReadToEnd().ShouldEqual("aa\r\n");
        }

        [TestMethod]
        public void TestPipedGrep()
        {
            Log.WriteLine("******** TestPipedGrep starting *********");

            var command = (
                Command.Run("SampleCommand", "grep", "a") < new[] { "abcd", "a", "ab", "abc" }
                | Command.Run("SampleCommand", "grep", "b")
                | Command.Run("SampleCommand", "grep", "c") 
            );

            var results = command.StandardOutput.GetLines().ToArray();

            results.SequenceEqual(new[] { "abcd", "abc" }).ShouldEqual(true);
        }

        [TestMethod]
        public void TestLongWriteWithInfrequentReads()
        {
            var lines = Enumerable.Range(0, 100).Select(i => i.ToString())
                .ToArray();

            var command = Command.Run("SampleCommand", "grep", ".") < lines;
            var outputLines = new List<string>();
            var readTask = Task.Run(() =>
            {
                var rand = new Random(12345);
                while (true)
                {
                    if (rand.Next(10) == 0)
                    {
                        Thread.Sleep(200);
                    }
                    else
                    {
                        var line = command.StandardOutput.ReadLine();
                        if (line == null)
                        {
                            return;
                        }
                        else
                        {
                            outputLines.Add(line);
                        }
                    }
                }
            });

            Task.WaitAll(command.Task, readTask);

            string.Join("/", outputLines).ShouldEqual(string.Join("/", lines));
        }

        [TestMethod]
        public void TestHead()
        {
            var shell = new Shell(o => o.StartInfo(si => si.RedirectStandardError = false));
            var command = shell.Run("SampleCommand", "head", "10") < Enumerable.Range(0, 100).Select(i => i.ToString());
            command.Task.Result.StandardOutput.Trim().ShouldEqual(string.Join(Environment.NewLine, Enumerable.Range(0, 10)));
        }

        [TestMethod]
        public void TestCloseStandardOutput()
        {
            var shell = new Shell(o => o.StartInfo(si => si.RedirectStandardError = false));
            var command = shell.Run("SampleCommand", "grep", "a") < Enumerable.Repeat(new string('a', 1000), 1000);
            command.StandardOutput.BaseStream.ReadByte();
            command.StandardOutput.BaseStream.Dispose();
            
            UnitTestHelpers.AssertThrows<ObjectDisposedException>(() => command.StandardOutput.BaseStream.ReadByte());
            UnitTestHelpers.AssertThrows<ObjectDisposedException>(() => command.StandardOutput.ReadToEnd());

            var command2 = shell.Run("SampleCommand", "grep", "a") < Enumerable.Repeat(new string('a', 1000), 1000);
            command2.Wait();
            command2.StandardOutput.Dispose();
            UnitTestHelpers.AssertThrows<ObjectDisposedException>(() => command2.StandardOutput.Read());
        }

        [TestMethod]
        public void TestExitCode()
        {
            if (!Command.Run("SampleCommand", "exit", 0).Result.Success)
            {
                Assert.Fail("Should have worked");
            }
            if (Command.Run("SampleCommand", "exit", 1).Result.Success)
            {
                Assert.Fail("Should have failed");
            }

            var shell = new Shell(o => o.ThrowOnError());
            var ex = UnitTestHelpers.AssertThrows<AggregateException>(() => shell.Run("SampleCommand", "exit", -1).Task.Wait());
            ex.InnerExceptions.Select(e => e.GetType()).SequenceEqual(new[] { typeof(ErrorExitCodeException) })
                .ShouldEqual(true);

            shell.Run("SampleCommand", "exit", 0).Task.Wait();
        }

        [TestMethod]
        public void TestTimeout()
        {
            var willTimeout = Command.Run("SampleCommand", new object[] { "sleep", 1000000 }, o => o.Timeout(TimeSpan.FromMilliseconds(200)));
            var ex = UnitTestHelpers.AssertThrows<AggregateException>(() => willTimeout.Task.Wait());
            Assert.IsInstanceOfType(ex.InnerException, typeof(TimeoutException));
        }

        [TestMethod]
        public void TestErrorHandling()
        {
            var command = Command.Run("SampleCommand", "echo") < "abc" > new char[0];
            UnitTestHelpers.AssertThrows<AggregateException>(() => command.Wait());

            var command2 = Command.Run("SampleCommand", "echo") < this.ErrorLines();
            UnitTestHelpers.AssertThrows<AggregateException>(() => command.Wait());
        }

        [TestMethod]
        public void TestStopBufferingAndDiscard()
        {
            var command = Command.Run("SampleCommand", "pipe");
            command.StandardOutput.StopBuffering();
            var line = new string('a', 100);
            var state = 0;
            while (state < 2)
            {
                Log.WriteLine("Write to unbuffered command");
                var task = command.StandardInput.WriteLineAsync(line);
                if (!task.Wait(TimeSpan.FromSeconds(1)))
                {
                    if (state == 0)
                    {
                        Log.WriteLine("Buffer full: read one line");
                        var outLine = command.StandardOutput.ReadLine();
                        outLine.ShouldEqual(line);
                        // after this, we need to read more than one line to re-block since the readers buffer internally
                    }
                    else
                    {
                        Log.WriteLine("Buffer full: discard content");
                        command.StandardOutput.Discard();
                    }
                    
                    task.Wait(TimeSpan.FromSeconds(.5)).ShouldEqual(true, "can finish after read");
                    if (state == 1)
                    {
                        command.StandardInput.Close();
                    }
                    state++;
                }
            }
        }

        private IEnumerable<string> ErrorLines()
        {
            yield return "1";
            throw new InvalidOperationException("Can't enumerate");
        }

        // TODO error handling tests
    }
}