﻿// -----------------------------------------------------------------------
// <copyright file="DocumentRepository.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Microsoft.Partner.SmartOffice.Data
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Net;
    using System.Reflection;
    using System.Threading.Tasks;
    using Azure.Documents;
    using Azure.Documents.Client;
    using Azure.Documents.Linq;
    using Models.Converters;
    using Newtonsoft.Json;

    public class DocumentRepository<TEntity> : IDocumentRepository<TEntity> where TEntity : class
    {
        /// <summary>
        /// Path for the BulkImport.js embedded resource.
        /// </summary>
        private const string BulkImportEmbeddedResource = "Microsoft.Partner.SmartOffice.Data.Scripts.BulkImport.js";

        /// <summary>
        /// Name of the bulk import stored procedure.
        /// </summary>
        private const string BulkImportStoredProcId = "BulkImport";

        /// <summary>
        /// Access key used for authentication purposes.
        /// </summary>
        private readonly string authKey;

        /// <summary>
        /// Identifier of the collection for the repository.
        /// </summary>
        private readonly string collectionId;

        /// <summary>
        /// Identifier of the database forthe  repository.
        /// </summary>
        private readonly string databaseId;

        /// <summary>
        /// Endpoint address for the instance of Cosmos DB.
        /// </summary>
        private readonly string serviceEndpoint;

        /// <summary>
        /// Provides the ability to interact with Cosmos DB.
        /// </summary>
        private static IDocumentClient documentClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentRepository{TEntity}" /> class.
        /// </summary>
        /// <param name="serviceEndpoint">Service address for the instance of Cosmos DB.</param>
        /// <param name="authKey">Access key used for authentication purposes.</param>
        /// <param name="databaseId">Identifier of the databse for this repository.</param>
        /// <param name="collectionId">Identifier of the collection for this repository.</param>
        public DocumentRepository(string serviceEndpoint, string authKey, string databaseId, string collectionId)
        {
            this.authKey = authKey;
            this.collectionId = collectionId;
            this.databaseId = databaseId;
            this.serviceEndpoint = serviceEndpoint;
        }

        /// <summary>
        /// Initializes a new instance fo the <see cref="DocumentRepository{TEntity}" /> class.
        /// </summary>
        /// <param name="client">Client used to perform operations against Cosmos DB.</param>
        /// <param name="databaseId">Identifier of the databse for this repository.</param>
        /// <param name="collectionId">Identifier of the collection for this repository.</param>
        public DocumentRepository(IDocumentClient client, string databaseId, string collectionId)
        {
            documentClient = client;
            this.collectionId = collectionId;
            this.databaseId = databaseId;
        }

        /// <summary>
        /// Provides the ability to interact with Cosmos DB.
        /// </summary>
        private IDocumentClient Client
        {
            get
            {
                if (documentClient == null)
                {
                    documentClient = new DocumentClient(
                        new Uri(serviceEndpoint),
                        authKey,
                        new JsonSerializerSettings
                        {
                            Converters = new List<JsonConverter>
                            {
                                {
                                    new EnumJsonConverter()
                                }
                            },
                            DateFormatHandling = DateFormatHandling.IsoDateFormat,
                            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                            NullValueHandling = NullValueHandling.Ignore,
                            ReferenceLoopHandling = ReferenceLoopHandling.Serialize
                        },
                        new ConnectionPolicy
                        {
                            ConnectionMode = ConnectionMode.Direct,
                            ConnectionProtocol = Protocol.Tcp
                        });
                }

                return documentClient;
            }
        }

        /// <summary>
        /// Add or update an item in the repository.
        /// </summary>
        /// <param name="item">The item to be added or updated.</param>
        /// <returns>The entity that was added or updated.</returns>
        public async Task<TEntity> AddOrUpdateAsync(TEntity item)
        {
            ResourceResponse<Document> response;

            try
            {
                response = await Client.UpsertDocumentAsync(
                    UriFactory.CreateDocumentCollectionUri(databaseId, collectionId), item).ConfigureAwait(false);

                return (TEntity)(dynamic)response.Resource;
            }
            finally
            {
                response = null;
            }
        }

        /// <summary>
        /// Add or update the collection of items in the repository.
        /// </summary>
        /// <param name="items">A collection of items to be added or updated.</param>
        /// <returns>
        /// An instance of the <see cref="Task" /> class that represents the asynchronous operation.
        /// </returns>
        public async Task AddOrUpdateAsync(IEnumerable<TEntity> items)
        {
            await Client.ExecuteStoredProcedureAsync<int>(
                UriFactory.CreateStoredProcedureUri(
                    databaseId,
                    collectionId,
                    BulkImportStoredProcId),
                items).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets an item from the repository.
        /// </summary>
        /// <param name="id">Identifier of the item.</param>
        /// <returns>
        /// The item that matches the specified identifier; if not found null.
        /// </returns>
        public async Task<TEntity> GetAsync(string id)
        {
            Document document;

            try
            {
                document = await Client.ReadDocumentAsync(
                    UriFactory.CreateDocumentUri(databaseId, collectionId, id)).ConfigureAwait(false);

                return (TEntity)(dynamic)document;
            }
            catch (DocumentClientException ex)
            {
                if (ex.StatusCode != HttpStatusCode.NotFound)
                {
                    throw;
                }

                return null;
            }
            finally
            {
                document = null;
            }
        }

        /// <summary>
        /// Gets all items available in the repository.
        /// </summary>
        /// <returns>
        /// A collection of items that represent the items in the repository.
        /// </returns>
        public async Task<List<TEntity>> GetAsync()
        {
            FeedResponse<dynamic> response;
            List<TEntity> results;

            try
            {
                response = await Client.ReadDocumentFeedAsync(
                    UriFactory.CreateDocumentCollectionUri(databaseId, collectionId)).ConfigureAwait(false);

                results = response.Select(d => (TEntity)d).ToList();

                return results;
            }
            finally
            {
                response = null;
            }
        }

        /// <summary>
        /// Gets a sequence of items for the repository that matches the query. 
        /// </summary>
        /// <param name="predicate">A function to test each element for a condition.</param>
        /// <returns>
        /// A collection that contains items from the repository that satisfy the condition specified by predicate.
        /// </returns>
        public async Task<List<TEntity>> GetAsync(Expression<Func<TEntity, bool>> predicate)
        {
            IDocumentQuery<TEntity> query;
            List<TEntity> results;

            try
            {
                query = Client.CreateDocumentQuery<TEntity>(
                    UriFactory.CreateDocumentCollectionUri(databaseId, collectionId))
                        .Where(predicate)
                        .AsDocumentQuery();

                results = new List<TEntity>();

                while (query.HasMoreResults)
                {
                    results.AddRange(await query.ExecuteNextAsync<TEntity>().ConfigureAwait(false));
                }

                return results;
            }
            finally
            {
                query = null;
            }
        }

        /// <summary>
        /// Performs the initialization operations for the repository.
        /// </summary>
        /// <returns>
        /// An instance of the <see cref="Task" /> class that represents the asynchronous operation.
        /// </returns>
        public async Task InitializeAsync()
        {
            await CreateDatabaseIfNotExistsAsync().ConfigureAwait(false);
            await CreateCollectionIfNotExistsAsync().ConfigureAwait(false);
            await CreateStoredProcedureIfNotExistsAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Creates the collection if it does not already exists.
        /// </summary>
        /// <returns>
        /// An instance of the <see cref="Task" /> class that represents the asynchronous operation.
        /// </returns>
        private async Task CreateCollectionIfNotExistsAsync()
        {
            try
            {
                await Client.ReadDocumentCollectionAsync(
                    UriFactory.CreateDocumentCollectionUri(databaseId, collectionId)).ConfigureAwait(false);
            }
            catch (DocumentClientException ex)
            {
                if (ex.StatusCode != HttpStatusCode.NotFound)
                {
                    throw;
                }

                await Client.CreateDocumentCollectionAsync(
                    UriFactory.CreateDatabaseUri(databaseId),
                    new DocumentCollection
                    {
                        Id = collectionId
                    },
                    new RequestOptions { OfferThroughput = 400 }).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates the database if it does not already exists.
        /// </summary>
        /// <returns>
        /// An instance of the <see cref="Task" /> class that represents the asynchronous operation.
        /// </returns>
        private async Task CreateDatabaseIfNotExistsAsync()
        {
            try
            {
                await Client.ReadDatabaseAsync(UriFactory.CreateDatabaseUri(databaseId)).ConfigureAwait(false);
            }
            catch (DocumentClientException ex)
            {
                if (ex.StatusCode != HttpStatusCode.NotFound)
                {
                    throw;
                }

                await Client.CreateDatabaseAsync(
                    new Database { Id = databaseId }).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates the stored procedure if it does not already exists.
        /// </summary>
        /// <returns>
        /// An instance of the <see cref="Task" /> class that represents the asynchronous operation.
        /// </returns>
        private async Task CreateStoredProcedureIfNotExistsAsync()
        {
            string storedProc;

            try
            {
                await Client.ReadStoredProcedureAsync(
                    UriFactory.CreateStoredProcedureUri(
                        databaseId,
                        collectionId,
                        BulkImportStoredProcId)).ConfigureAwait(false);
            }
            catch (DocumentClientException ex)
            {
                if (ex.StatusCode != HttpStatusCode.NotFound)
                {
                    throw;
                }

                using (StreamReader reader = new StreamReader(
                    Assembly.GetExecutingAssembly().GetManifestResourceStream(BulkImportEmbeddedResource)))
                {
                    storedProc = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                await Client.CreateStoredProcedureAsync(
                    UriFactory.CreateDocumentCollectionUri(databaseId, collectionId),
                    new StoredProcedure
                    {
                        Body = storedProc,
                        Id = BulkImportStoredProcId
                    }).ConfigureAwait(false);
            }
        }
    }
}