---
name: feedback-turkish-comments-and-commits
description: Kullanıcı tüm kod açıklamalarının, tüm git commit mesajlarının VE kullanıcıya yazılan tüm sohbet yanıtlarının sadece Türkçe olmasını istiyor, her repoda. Herhangi bir açıklama, commit mesajı veya sohbet yanıtı yazmadan önce bunu oku.
metadata:
  type: feedback
---

# Kod açıklamaları, commit mesajları VE sohbet yanıtları sadece Türkçe olacak (2026-09-19, aynı gün pekiştirildi)

Kullanıcının ilk talimatı, oturum ortasında: "kod açıklamalarını türkçeye çevir her zaman türkçe yazsın commit satırları".

**Aynı gün, daha sert şekilde pekiştirildi, Claude sohbette İngilizce yanıt vermeye devam edince:** "bir daha uyarmayacağım seni bana sadece türkçe açıklama yaz. kod stırına da sadece türkçe açıklama yaz". Bu, bu hafıza dosyasının kendisinde daha önce kaydedilmiş YANLIŞ bir varsayımın düzeltilmesidir - "sohbet yanıtları konuşmaya uyan herhangi bir dilde kalabilir" varsayımı yanlıştı ve gerçek, tekrarlanan bir rahatsızlığa yol açtı.

**Ayrıca kullanıcı, bu kuralın kaydedildiği hafıza dosyasının kendisinin İngilizce yazılmış olmasını da ayrıca eleştirdi** ("son işlem ne olum onu niye çevirmedin") - yani bu kural sadece kod/commit/sohbete değil, Claude'un yazdığı HER açıklayıcı metne (hafıza dosyaları dahil) uygulanmalı.

**Nasıl uygulanır, güncel/son hâli:**
- Bundan sonra yazılan her kod açıklaması (hangi dosyada, hangi repoda olursa olsun) Türkçe olacak - GymApp'te zaten var olan yerleşik kurala (bkz. o repodaki "Translate code comments to Turkish" commit'i, `85b9483`) uyuyor, ama artık GymAppApi'ye de açıkça genişletildi (o repo baştan sona İngilizceydi).
- Bundan sonra her git commit mesajının başlığı/gövdesi Türkçe olacak, hem `GymAppApi`'de hem `GymApp`'te. Bu, commit'lerin Claude/AI imzası taşımaması kuralını değiştirmiyor (bkz. bu projenin CLAUDE.md'si) - sadece mesajın dilini değiştiriyor.
- **Kullanıcıya verilen her sohbet yanıtı, hangi repo/oturumda olursa olsun, sadece Türkçe olacak - istisnasız, İngilizce karıştırılmadan.** Bu, araç tanımlarının veya diğer sistem metinlerinin hangi dili kullandığından bağımsız geçerlidir.
- **Claude'un yazdığı hafıza dosyaları da dahil olmak üzere HER açıklayıcı metin Türkçe olacak.** Bu kural sadece repo içine giren içerikle (kod, commit) veya sadece sohbetle sınırlı değil - memory sistemine yazılan her yeni dosya/güncelleme de Türkçe yazılmalı.
- Bu kural bundan sonraki YENİ commit'lere/açıklamalara/hafıza kayıtlarına uygulanır. GymAppApi'deki mevcut İngilizce açıklama geçmişinin tamamını geriye dönük çevirmek ayrı, büyük ve henüz istenmemiş bir iştir - istenmeden kapsamlı bir geçiş yapma, ama zaten İngilizce açıklamalar bulunan bir dosyayı düzenlerken onları olduğu gibi bırakmak da sorun değil (istenmedikçe).
