# Peta fitur: aplikasi lama -> aplikasi baru

Dibuat otomatis oleh `tools/Coverage/make_coverage.py` dari menu `RFMENUX` (DB LOGIN dev), `CONTROL_MENU` (DB DATA dev), dan halaman Blazor yang ada.
Data menu dan kontrol berasal dari DB **dev**; menu/kontrol di produksi bisa berbeda dan harus dicocokkan ulang (jalankan ulang skrip dengan ekspor dari produksi).

## 1. Menu

**37 dari 40** menu yang punya halaman (modul 60, 70, 99) sudah punya halaman baru.

| Modul | Menu | URL lama | Status | Halaman baru |
|---|---|---|---|---|
| 60 Collection | Distribusi Debitur | `~/Main/MainFacility.aspx?FUNC=worklistmonitoring` | Selesai | `src/Panin.Web/Modules/Collection/Distribution/WorklistMonitoring.razor` |
| 60 Collection | Monitoring Debitur | `~/Main/MainFacility.aspx?FUNC=accmon` | Selesai | `src/Panin.Web/Modules/Collection/Monitoring/AccountMonitoring.razor` |
| 60 Collection | Monitoring Aktifitas Collection | `~/controls/Search_activity_collector.aspx` | Selesai | `src/Panin.Web/Modules/Collection/Monitoring/ActivityMonitoring.razor` |
| 60 Collection | Monitoring Recovery/NPL | `~/Main/MainRecovery.aspx?FUNC=recmon` | Selesai | `src/Panin.Web/Modules/Collection/Monitoring/RecoveryMonitoring.razor` |
| 60 Collection | Account Exception Letter | `~/Main/MainFacility.aspx?FUNC=ExceptionLetter` | Selesai | `src/Panin.Web/Modules/Collection/Distribution/ExceptionLetter.razor` |
| 60 Collection | Bulk Surat (New) | `~/Facilities/BulkSurat.aspx` | Selesai | `src/Panin.Web/Modules/Collection/Distribution/BulkLetter.razor` |
| 60 Collection | Report | `~/Report/ReportSet.aspx?setid=gen` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 60 Collection | Daily Tracking | `~/Report/ReportSet.aspx?setid=DT` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 60 Collection | Leading Edge | `~/Report/ReportSet.aspx?setid=LE` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 60 Collection | Lag Delinquency | `~/Report/ReportSet.aspx?setid=LD` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 60 Collection | Daily Tracking PRK | `~/Report/ReportSet.aspx?setid=PRK` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 60 Collection | Report Khusus KPM | `~/Report/ReportSet.aspx?setid=KPM` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 60 Collection | Flow Rate | `~/Report/ReportSet.aspx?setid=FR` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 60 Collection | Was Is Reprot | `~/Report/ReportSet.aspx?setid=WASIS` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 60 Collection | Print Surat Kuasa Penarikan Agunan Mobil | `~/Controls/CariSKPM.aspx` | Selesai | `src/Panin.Web/Modules/Collection/Memos/MemoSearch.razor` |
| 60 Collection | Print Memo Pengajuan Hapus Buku | `~/Controls/CariHapusBuku.aspx` | Selesai | `src/Panin.Web/Modules/Collection/Memos/MemoSearch.razor` |
| 60 Collection | Print Memo Pengajuan Hapus Tagih | `~/Controls/CariHapusTagih.aspx` | Selesai | `src/Panin.Web/Modules/Collection/Memos/MemoSearch.razor` |
| 60 Collection | Print Memo Pengajuan Restrukturisasi | `~/Controls/CariRestrukturisasi.aspx` | Selesai | `src/Panin.Web/Modules/Collection/Memos/MemoSearch.razor` |
| 60 Collection | Print Memo Pengajuan Penyelesaian AYDA | `~/Controls/CariAYDA.aspx` | Selesai | `src/Panin.Web/Modules/Collection/Memos/MemoSearch.razor` |
| 60 Collection | Change Password | `~/ChangePassword.aspx` | Selesai | `src/Panin.Web/Modules/Account/ChangePasswordPage.razor` |
| 60 Collection | Inquiry Status Debitur | `~/controls/search_debitur.aspx` | Selesai | `src/Panin.Web/Modules/Collection/Inquiry/DebiturSearch.razor` |
| 60 Collection | Inquiry Data AYDA/WO | `~/controls/search_data_ayda.aspx` | **BELUM** |  |
| 60 Collection | Add News | `~/Main/MainFacility.aspx?FUNC=news&status=add` | **BELUM** |  |
| 60 Collection | News View | `~/Main/MainFacility.aspx?FUNC=news&status=view` | Selesai | `src/Panin.Web/Modules/Collection/News/NewsView.razor` |
| 60 Collection | Simulasi | `~/Simulasi/Search_Debitur.aspx` | **BELUM** |  |
| 70 MIS Report | Report | `~/Report/ReportSet.aspx?setid=gen` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 70 MIS Report | Daily Tracking | `~/Report/ReportSet.aspx?setid=DT` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 70 MIS Report | Leading Edge | `~/Report/ReportSet.aspx?setid=LE` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 70 MIS Report | Lag Delinquency | `~/Report/ReportSet.aspx?setid=LD` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 70 MIS Report | Daily Tracking PRK | `~/Report/ReportSet.aspx?setid=PRK` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 70 MIS Report | Report Khusus KPM | `~/Report/ReportSet.aspx?setid=KPM` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 70 MIS Report | Flow Rate | `~/Report/ReportSet.aspx?setid=FR` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 70 MIS Report | Was Is Reprot | `~/Report/ReportSet.aspx?setid=WASIS` | Selesai | `src/Panin.Web/Modules/Reports/ReportCatalog.razor` |
| 99 Maintenance | Maker | `~/parameter/ParamSet.aspx?set=USER&ismaker=1&title=User and Group P...` | Selesai | `src/Panin.Web/Modules/Maintenance/ParameterSetMaker.razor` |
| 99 Maintenance | Approval | `~/parameter/ParamSet.aspx?set=USER&ismaker=0&title=User and Group P...` | Selesai | `src/Panin.Web/Modules/Maintenance/ParameterSetApproval.razor` |
| 99 Maintenance | Maker | `~/parameter/ParamSet.aspx?set=GEN&ismaker=1&title=Collection Genera...` | Selesai | `src/Panin.Web/Modules/Maintenance/ParameterSetGenMaker.razor` |
| 99 Maintenance | Approval | `~/parameter/ParamSet.aspx?set=GEN&ismaker=0&title=Collection Genera...` | Selesai | `src/Panin.Web/Modules/Maintenance/ParameterSetGenApproval.razor` |
| 99 Maintenance | Maker | `~/parameter/ParamSet.aspx?set=PROC&ismaker=1&title=Collection Proce...` | Selesai | `src/Panin.Web/Modules/Maintenance/ParameterSetProcMaker.razor` |
| 99 Maintenance | Approval | `~/parameter/ParamSet.aspx?set=PROC&ismaker=0&title=Collection Proce...` | Selesai | `src/Panin.Web/Modules/Maintenance/ParameterSetProcApproval.razor` |
| 99 Maintenance | Change Password | `~/ChangePassword.aspx` | Selesai | `src/Panin.Web/Modules/Account/ChangePasswordPage.razor` |

## 2. Fungsi layar detail dan monitoring (`CONTROL_MENU`)

Setiap baris adalah satu fungsi yang bisa dipanggil layar lama (`GET_CONTROL`). "Tidak ada kode" = entri ada di tabel tetapi berkasnya tidak ada di repo (warisan LOS) atau tidak aktif; **konfirmasi ke pengguna bahwa fungsi itu memang tidak dipakai**.

| Fungsi | Kontrol lama | Status | Keterangan |
|---|---|---|---|
| `00020` | `personalinfo.ascx` | Sebagian | Data nasabah tampil di kartu Nasabah; penyuntingan hanya telepon/alamat lewat bagian Hasil Call/Visit |
| `00021` | `personaljob.ascx` | Sebagian | Idem (pekerjaan): tampil, penyuntingan penuh belum |
| `00022` | `spouseinfo.ascx` | Sebagian | Idem (pasangan): tampil, penyuntingan penuh belum |
| `00023` | `spousejob.ascx` | Sebagian | Idem (pekerjaan pasangan): tampil, penyuntingan penuh belum |
| `accmon` | `AccountMonitoring.ascx` | Selesai | Monitoring Debitur |
| `Account` | `AccFinancialInfo.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `AccountOther` | `AccFinancialInfoOther.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `AccRel` | `AccFinancialInfo.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `accsrcmon` | `AccountSearchMonitoring.ascx` | **BELUM** | Search Recovery (`AccountSearchMonitoring.ascx`) |
| `agunan` | `AgunanKredit.ascx` | **BELUM** | Agunan kredit (`AgunanKredit.ascx`), termasuk unggah dan lihat gambar agunan |
| `agunanCAR` | `VerticalGrid.ascx` | **BELUM** | Agunan mobil/motor |
| `agunanDEP` | `VerticalGrid.ascx` | **BELUM** | Agunan lain-lain |
| `agunanTNH` | `VerticalGrid.ascx` | **BELUM** | Agunan tanah dan bangunan |
| `asuransijiwa` | `asuransijiwa.ascx` | **BELUM** | Asuransi jiwa |
| `asuransikerugian` | `asuransikerugian.ascx` | **BELUM** | Asuransi kerugian |
| `Call` | `Call.ascx` | Selesai | Form Call (`/collection/debitur/call`) termasuk bagian Hasil |
| `callrec` | `call_rec.ascx` | **BELUM** | Call versi Recovery (`Call_Rec.ascx`) |
| `Cheque` | `NULL` | Tidak ada kode | Tidak aktif (`ACTIVE=0`) |
| `Contact` | `CustContactInfo.ascx` | **BELUM** | Contact info (`CustContactInfo.ascx`) |
| `CorespAddr` | `CoresspAddr.ascx` | **BELUM** | Edit alamat korespondensi |
| `Cust` | `CustSumOfAccount.ascx` | Tidak ada kode | Tidak aktif (`ACTIVE=0`) |
| `CustInfo` | `CustGeneralInfo.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `customeraddresschange` | `CustAddressChange.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `customersearch` | `CustomerSearch.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `DetailTunggakan` | `DetailTunggakan.ascx` | **BELUM** | Panel Detail Tunggakan di layar detail (laporan Detail Tunggakan sudah ada di menu Report) |
| `ExceptionLetter` | `LetterException.ascx` | Selesai | Account Exception Letter |
| `Ledger` | `MainLedger.ascx` | Tidak ada kode | Tidak aktif (`ACTIVE=0`) |
| `Legal` | `Legal.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `Lelang` | `BalaiLelang.ascx` | **BELUM** | Balai Lelang (`SAVE_DATA_LELANG`) dan History Lelang |
| `Letter` | `Letter.ascx` | Selesai | Form Letter (`/collection/debitur/letter`) |
| `list` | `List.ascx` | **BELUM** | Recovery Kelolaan (`List.ascx`) |
| `MAINCOL` | `Collection.ascx` | Selesai | Digantikan oleh layar detail debitur (`/collection/debitur/detail`) |
| `Meeting` | `Meeting.ascx` | **BELUM** | Meeting |
| `monhist` | `MonitoringHistory.ascx` | **BELUM** | Monitoring History |
| `news` | `News.ascx` | Sebagian | News View selesai; Add News (editor HTML, unggah gambar) dan edit berita belum |
| `notes` | `Notes.ascx` | **BELUM** | Notes (`SP_GET_NOTES`, `SP_INSERT_NOTES`) |
| `OtherHist` | `AccrInterestHistory.ascx` | **BELUM** | History accrued interest (`History/AccrInterestHistory.ascx`) |
| `Payment Schedule` | `PaymentSchedule.ascx` | **BELUM** | Payment schedule |
| `PayOff` | `PayOff.ascx` | **BELUM** | Pay off |
| `Properti` | `AccPropertyInfo.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `RecHist` | `RecoveryHistory.ascx` | Tidak ada kode | Tidak aktif (`ACTIVE=0`) |
| `recmon` | `RecoveryMonitoring.ascx` | Selesai | Monitoring Recovery/NPL (tautan Janji Debitur dan View News ada; Upload Data AYDA, Upload Visit belum) |
| `Recovery` | `Recovery.ascx` | **BELUM** | Workflow Recovery (`Recovery.ascx`, `Treatment_Recovery`, `Information_Recovery`, halaman `Recovery/*`) |
| `Relation` | `RelationInfo.ascx` | **BELUM** | Relation account |
| `REPOPROPERTI` | `AccPropertyInfo.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `REPOSEARCH` | `RepoSearch.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `Repossessed Item` | `RepoItem.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `Repossession Order` | `RepoOrder.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `REPOVEHICLE` | `AccVehicleInfo.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `riwayatbayar` | `RiwayatPembayaran.ascx` | **BELUM** | Riwayat pembayaran (`SP_RIWAYAT_BAYAR_LIHAT`) |
| `Skip Tracing` | `SkipTracing.ascx` | **BELUM** | Skip tracing |
| `SMS` | `SMS.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `Spooler Pending` | `Spooler.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `Statistic` | `CustCustStatistic.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `Transfer` | `AccTransfer.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `Vehicle` | `AccVehicleInfo.ascx` | Tidak ada kode | Berkas tidak ada di repo |
| `Visitation` | `Visit.ascx` | Selesai | Form Visit (`/collection/debitur/visit`) |
| `visitrec` | `visit_rec.ascx` | **BELUM** | Visit versi Recovery (`visit_rec.ascx`) |
| `worklistmonitoring` | `WorklistMonitoring.ascx` | Selesai | Distribusi Debitur: pindah debitur, redistribusi otomatis (template/index), dan Generate Excel agen visit |

Ringkasan: Selesai 8, Sebagian 5, **BELUM 24**, tidak ada kode 22.
