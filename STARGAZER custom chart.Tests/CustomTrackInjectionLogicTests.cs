using System;
using System.Collections.Generic;
using Xunit;

namespace STARGAZER_custom_chart.Tests
{
    public sealed class CustomTrackInjectionLogicTests
    {
        [Fact]
        public void FirstSet_AllAlbumsNeedCloning()
        {
            IntPtr?[] clones = { null, null, null };

            List<int> missing = CustomTrackInjectionLogic.FindAlbumsMissingFromList(clones, new HashSet<IntPtr>());

            Assert.Equal(new[] { 0, 1, 2 }, missing);
        }

        [Fact]
        public void NewListWithoutClones_ReinsertsExistingClonesInsteadOfRecloning()
        {
            // 정렬/필터로 새 목록이 들어오면 기존 복제본은 목록에 없지만 "다시 넣기"만 하면 된다.
            IntPtr?[] clones = { new IntPtr(0x10), new IntPtr(0x20) };

            List<int> missing = CustomTrackInjectionLogic.FindAlbumsMissingFromList(clones, new HashSet<IntPtr>());

            Assert.Equal(new[] { 0, 1 }, missing);
        }

        [Fact]
        public void ClonesAlreadyInList_AreNotInsertedAgain()
        {
            // 첫 항목이 아니어도(정렬된 목록 중간) 이미 들어 있는 복제본은 중복 삽입하지 않는다.
            IntPtr?[] clones = { new IntPtr(0x10), new IntPtr(0x20), null };
            var present = new HashSet<IntPtr> { new IntPtr(0x20) };

            List<int> missing = CustomTrackInjectionLogic.FindAlbumsMissingFromList(clones, present);

            Assert.Equal(new[] { 0, 2 }, missing);
        }

        [Fact]
        public void EverythingPresent_NothingToDo()
        {
            IntPtr?[] clones = { new IntPtr(0x10), new IntPtr(0x20) };
            var present = new HashSet<IntPtr> { new IntPtr(0x10), new IntPtr(0x20) };

            Assert.Empty(CustomTrackInjectionLogic.FindAlbumsMissingFromList(clones, present));
        }
    }
}
