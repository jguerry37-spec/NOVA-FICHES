use strict; use warnings;
local $/ = undef;
my $file = shift @ARGV;
open my $fh, '<', $file or die $!;
my $txt = <$fh>; close $fh;
my $new = <<'HTML';
<div class="infoGrid3" style="margin-top:10px;">
  <div class="field span-3">
    <div class="small">Nom de l’intervention</div>
    <input id="repElements" class="box" type="text" placeholder="IMPLANTATION N10" />
  </div>

  <div class="field span-3">
    <div class="small">Ville</div>
    <input id="repZone" class="box" type="text" placeholder="Paris 16" />
  </div>

  <div class="field span-2">
    <div class="small">Adresse chantier</div>
    <input id="repSiteAddress" class="box" type="text" placeholder="104 Avenue du Président Kennedy" />
  </div>
  <div class="field span-1">
    <div class="small">Contact chantier</div>
    <input id="repSiteContact" class="box" type="text" placeholder="Nom / Téléphone" />
  </div>

  <div class="field span-1">
    <div class="small">N° CHA</div>
    <input id="repCHA" class="box" type="text" placeholder="CHA02782" />
  </div>
  <div class="field span-1">
    <div class="small">Date d’intervention</div>
    <input id="repDate" class="box" type="date" />
  </div>
  <div class="field span-1">
    <div class="small">Entreprise</div>
    <input id="repClient" class="box" type="text" placeholder="LOGISUR" />
  </div>

  <div class="field span-1">
    <div class="small">Phase</div>
    <select id="repPhase" class="box">
      <option value="REC">REC</option>
      <option value="IMP">IMP</option>
      <option value="CTRL">CTRL</option>
      <option value="LEV">LEV</option>
    </select>
  </div>
  <div class="field span-1">
    <div class="small">Type</div>
    <input id="repType" class="box" type="text" placeholder="TYPE" />
  </div>
  <div class="field span-1">
    <div class="small">Indice</div>
    <input id="repIndice" class="box" type="text" placeholder="A" />
  </div>

  <div class="field span-3">
    <div class="small">Plan de référence (DWG)</div>
    <input id="repDwg" class="box" type="text" placeholder="xxxxx.dwg" />
  </div>
</div>
HTML

$txt =~ s{(<h2>Infos dossier \(cartouche\)</h2>\s*)(.*?)(\s*<!-- Référentiel projet)}{$1.$new."\n\n      ".$3}es;
open my $out, '>', $file or die $!;
print $out $txt;
close $out;
