using System.Collections.Generic;
using System.Linq;

namespace FanBento.Database.Models
{
    public static class Utils
    {
        /// <summary>
        ///     Modify posts in the list in-place to make sure all Post object with the same UserId refer to the same User object
        /// </summary>
        /// <param name="posts"></param>
        public static void UnifyUserReference(this List<Post> posts)
        {
            var users = posts.Select(t => t.User).Distinct(new UserEqualityComparer()).ToList();
            foreach (var post in posts) post.User = users.First(t => t.UserId == post.User.UserId);
        }

        private class UserEqualityComparer : IEqualityComparer<User>
        {
            public bool Equals(User x, User y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.UserId == y.UserId;
            }

            public int GetHashCode(User obj)
            {
                return obj.UserId != null ? obj.UserId.GetHashCode() : 0;
            }
        }
    }
}