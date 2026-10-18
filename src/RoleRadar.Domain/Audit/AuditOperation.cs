namespace RoleRadar.Domain.Audit;

public static class AuditOperation
{
    public const string NoteCreate = "note_create";
    public const string NoteAppend = "note_append";
    public const string NoteReplaceSection = "note_replace_section";
    public const string NoteArchive = "note_archive";
    public const string ImportCreate = "import_create";
    public const string ImportOverwrite = "import_overwrite";
    public const string ContactAdd = "contact_add";
    public const string ContactRedact = "contact_redact";

    public static bool IsValid(string operation) =>
        operation is NoteCreate or NoteAppend or NoteReplaceSection or NoteArchive or ImportCreate or ImportOverwrite
            or ContactAdd or ContactRedact;
}
