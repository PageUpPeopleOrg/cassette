using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Cassette.Caching;
using Cassette.Utilities;

namespace Cassette
{
    /// <summary>
    /// Partial implementation of IAsset that reads from an assembly resource stream.
    /// </summary>
    public class ResourceAsset : IAsset
    {
        readonly string resourceName;
        readonly Assembly assembly;
        readonly List<IAssetTransformer> transformers = new List<IAssetTransformer>();
        readonly List<AssetReference> references = new List<AssetReference>();
        readonly Bundle parentBundle;

        public ResourceAsset(string resourceName, Assembly assembly, Bundle parentBundle)
        {
            this.resourceName = resourceName;
            this.assembly = assembly;
            this.parentBundle = parentBundle;
        }

        public byte[] Hash
        {
            get
            {
                using (var stream = OpenStream())
                {
                    using (var sha1 = SHA1.Create())
                    {
                        return sha1.ComputeHash(stream);
                    }
                }
            }
        }

        public string Path
        {
            get { return "~/" + resourceName; }
        }

        public IEnumerable<AssetReference> References
        {
            get { return references; }
        }

        public void Accept(IBundleVisitor visitor)
        {
            visitor.Visit(this);
        }

        public void AddAssetTransformer(IAssetTransformer transformer)
        {
            transformers.Add(transformer);
        }

        public void AddReference(string assetRelativePath, int lineNumber)
        {
            if (assetRelativePath.IsUrl())
            {
                AddUrlReference(assetRelativePath, lineNumber);
            }
            else
            {
                string appRelativeFilename;
                if (assetRelativePath.StartsWith("~"))
                {
                    appRelativeFilename = assetRelativePath;
                }
                else if (assetRelativePath.StartsWith("/"))
                {
                    appRelativeFilename = "~" + assetRelativePath;
                }
                else
                {
                    return;
                    //throw new InvalidOperationException("Relative paths not supported " +
                    //                                    "for embedded resources.  Looked for: " +
                    //                                    "'" + assetRelativePath + "'");
                }
                appRelativeFilename = PathUtilities.NormalizePath(appRelativeFilename);
                AddBundleReference(appRelativeFilename, lineNumber);
            }
        }

        void AddBundleReference(string appRelativeFilename, int lineNumber)
        {
            var predicate = new BundleContainsPathPredicate(appRelativeFilename);
            Accept(predicate);
            var type = predicate.Result
                           ? AssetReferenceType.SameBundle
                           : AssetReferenceType.DifferentBundle;
            references.Add(new AssetReference(Path, appRelativeFilename, lineNumber, type));
        }

        void AddUrlReference(string url, int sourceLineNumber)
        {
            references.Add(new AssetReference(Path, url, sourceLineNumber, AssetReferenceType.Url));
        }

        public void AddRawFileReference(string relativeFilename)
        {
            if (relativeFilename.StartsWith("/"))
            {
                relativeFilename = "~" + relativeFilename;
            }
            else if (!relativeFilename.StartsWith("~"))
            {
                return;
                //throw new InvalidOperationException("Relative paths not supported " +
                //                                    "for embedded resources.  Looked for: " +
                //                                    "'" + relativeFilename + "'");
            }

            var alreadyExists = references.Any(r => r.ToPath.Equals(relativeFilename, StringComparison.OrdinalIgnoreCase));
            if (alreadyExists) return;

            references.Add(new AssetReference(Path, relativeFilename, -1, AssetReferenceType.RawFilename));
        }

        public Stream OpenStream()
        {
            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                stream.Close();
                var createStream = transformers.Aggregate<IAssetTransformer, Func<Stream>>(
                    () => assembly.GetManifestResourceStream(resourceName),
                    (current, transformer) => transformer.Transform(current, this)
                    );
                return createStream();
            }

            throw new InvalidOperationException(
                string.Format(
                              "Resource {0} not found in assembly {1}.",
                              resourceName,
                              assembly.FullName
                    )
                );
        }

        public Type AssetCacheValidatorType
        {
            get { return typeof(ResourceAssetCacheValidator); }
        }
    }
}