# license-gen — génération de licences Nova-Fiches

Outil interne Novatlas, **ne fait pas partie de l'application livrée**. Détient (ou génère)
la clé privée ECDSA utilisée pour signer les licences — à ne jamais copier dans le publish
de Nova-Fiches, ni committer dans Git.

## Première utilisation (une seule fois)

```powershell
dotnet run --project tools/license-gen -- genkey --out tools/license-gen/keys/private.key
```

- Écrit la clé **privée** dans `tools/license-gen/keys/private.key` (dossier exclu par `.gitignore`).
  **Faites-en immédiatement une copie de sauvegarde hors du dépôt** (coffre-fort d'entreprise,
  gestionnaire de mots de passe...). Si ce fichier est perdu, il faudra régénérer une nouvelle
  paire de clés et republier une nouvelle version de l'application (toutes les licences émises
  avec l'ancienne clé cesseront de fonctionner).
- Affiche la clé **publique** à coller dans `src/NovaFiches/Licensing/LicenseService.cs`
  (constante `PublicKeyBase64`). Déjà fait pour la clé actuelle — à refaire uniquement si la
  clé est régénérée.

## Générer une licence pour un poste

```powershell
dotnet run --project tools/license-gen -- issue --key tools/license-gen/keys/private.key --to "Nom du poste ou de l'utilisateur" --out license.json
```

Options :
- `--expires yyyy-MM-dd` : date d'expiration (optionnel — sans cette option, la licence n'expire jamais).
- `--machine <hash>` : lie la licence à un poste précis (optionnel — sans cette option, la licence
  fonctionne sur n'importe quel poste). Le hash à utiliser est celui affiché dans l'écran
  d'activation de l'utilisateur (bouton "Copier"), ou obtenu localement via `license-gen machineid`.

Envoyer le fichier `license.json` généré à l'utilisateur, qui l'installe via le bouton
"Sélectionner un fichier de licence…" au premier lancement de Nova-Fiches.

## Format de licence

Un fichier de licence est un JSON `{ "payload": "<base64>", "signature": "<base64>" }` où
`payload` est le payload canonique (voir `LicensePayloadFormat.cs`, dupliqué à l'identique
côté application et côté outil) signé en ECDSA P-256 / SHA-256.
