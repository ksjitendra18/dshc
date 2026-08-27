using CKYC.Core.Abstractions;
using CKYC.Core.Configuration;
using CKYC.Crm;
using CKYC.Data;
using CKYC.Files;
using CKYC.Fvu;

namespace CKYC.Processor;

/// <summary>
/// Composition root — builds every service once from the loaded settings. Kept explicit
/// (no container magic) so the wiring is easy to reason about and to swap to a DI
/// container later.
/// </summary>
public sealed class AppContext
{
    public AppContext(AppSettings settings)
    {
        Settings = settings;

        CustomerIds = new DailyCustomerIdProvider(settings.Source);
        Database = new SqlServerDatabase(settings.Database);
        Master = new MasterRepository(Database);
        Individuals = new IndividualRepository(Database);
        LegalEntities = new LegalEntityRepository(Database);
        Journal = new BatchJournal(Database);
        Search = new SearchRepository(Database);
        Updates = new UpdateRepository(Database);
        Downloads = new DownloadRepository(Database);
        IndividualDocuments = new IndividualDocumentStore(Database);
        LegalEntityDocuments = new LegalEntityDocumentStore(Database);
        Documents = IndividualDocuments;

        CrmData = new DummyCrmDataProvider();
        CrmLegalEntities = new DummyCrmLegalEntityProvider();
        Crm = new HttpCrmApiClient(settings.Crm);
        CrmServer = new CrmServer(CrmData, CustomerIds);

        Hasher = new FileHasher();
        BatchGenerator = new CkycBatchGenerator(settings.Batch, Hasher, Documents);
        LegalEntityBatchGenerator = new CkycLegalEntityBatchGenerator(settings.Batch, Hasher, Documents);
        SearchFileWriter = new CkycSearchWriter(settings.Search);
        IndividualUpdateWriter = new CkycIndividualUpdateWriter(settings.Update);
        LegalEntityUpdateWriter = new CkycLegalEntityUpdateWriter(settings.Update);
        Fvu = new FvuRunner(settings.Fvu, Hasher);
    }

    public AppSettings Settings { get; }
    public IDailyCustomerIdProvider CustomerIds { get; }
    public ICkycDatabase Database { get; }
    public IMasterRepository Master { get; }
    public IIndividualRepository Individuals { get; }
    public ILegalEntityRepository LegalEntities { get; }
    public IBatchJournal Journal { get; }
    public ISearchRepository Search { get; }
    public IUpdateRepository Updates { get; }
    public IDownloadRepository Downloads { get; }
    /// <summary>Per-client-type document stores. Documents routes to the individual store (default view).</summary>
    public IDocumentStore IndividualDocuments { get; }
    public IDocumentStore LegalEntityDocuments { get; }
    public IDocumentStore Documents { get; }

    public DummyCrmDataProvider CrmData { get; }
    public DummyCrmLegalEntityProvider CrmLegalEntities { get; }
    public ICrmApiClient Crm { get; }
    public CrmServer CrmServer { get; }

    public IFileHasher Hasher { get; }
    public IBatchGenerator BatchGenerator { get; }
    public ILegalEntityBatchGenerator LegalEntityBatchGenerator { get; }
    public ISearchFileWriter SearchFileWriter { get; }
    public CkycIndividualUpdateWriter IndividualUpdateWriter { get; }
    public CkycLegalEntityUpdateWriter LegalEntityUpdateWriter { get; }
    public IFvuRunner Fvu { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
        => await Database.InitializeSchemaAsync(ct);
}
