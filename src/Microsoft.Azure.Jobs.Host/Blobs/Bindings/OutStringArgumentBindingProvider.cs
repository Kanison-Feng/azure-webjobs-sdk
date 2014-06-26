﻿using System;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class OutStringArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (!parameter.IsOut || parameter.ParameterType != typeof(string).MakeByRefType())
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Write)
            {
                throw new InvalidOperationException("Cannot bind blob out string using access "
                    + access.Value.ToString() + ".");
            }

            return new StringArgumentBinding();
        }

        private class StringArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Write; }
            }

            public Type ValueType
            {
                get { return typeof(string); }
            }

            public IValueProvider Bind(ICloudBlob blob, FunctionBindingContext context)
            {
                CloudBlockBlob blockBlob = blob as CloudBlockBlob;

                if (blockBlob == null)
                {
                    throw new InvalidOperationException("Cannot bind a page blob using an out string.");
                }

                CloudBlobStream rawStream = blockBlob.OpenWrite();
                IBlobCommitedAction committedAction = new BlobCommittedAction(blob, context.FunctionInstanceId,
                    context.BlobWrittenWatcher);
                SelfWatchCloudBlobStream selfWatchStream = new SelfWatchCloudBlobStream(rawStream, committedAction);
                return new StringValueBinder(blob, selfWatchStream);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private sealed class StringValueBinder : IValueBinder, IWatchable
            {
                private readonly ICloudBlob _blob;
                private readonly SelfWatchCloudBlobStream _stream;

                public StringValueBinder(ICloudBlob blob, SelfWatchCloudBlobStream stream)
                {
                    _blob = blob;
                    _stream = stream;
                }

                public Type Type
                {
                    get { return typeof(string); }
                }

                public ISelfWatch Watcher
                {
                    get { return _stream; }
                }

                public object GetValue()
                {
                    return null;
                }

                public void SetValue(object value)
                {
                    string text = (string)value;

                    const int defaultBufferSize = 1024;

                    using (_stream)
                    {
                        using (TextWriter writer = new StreamWriter(_stream, Encoding.UTF8, defaultBufferSize,
                            leaveOpen: true))
                        {
                            writer.Write(text);
                        }

                        _stream.Commit();
                    }
                }

                public string ToInvokeString()
                {
                    return _blob.GetBlobPath();
                }
            }
        }
    }
}