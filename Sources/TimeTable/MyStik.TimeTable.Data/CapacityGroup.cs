﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStik.TimeTable.Data
{
    public class CapacityGroup
    {
        public CapacityGroup()
        {
            SemesterGroups = new HashSet<SemesterGroup>();
            Aliases = new HashSet<GroupAlias>();
        }


        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }

        /// <summary>
        /// Name der Gruppe nach Kapazität
        /// A, B, C oder 1, 2, 3 oder G1, G2, G3
        /// Muss nicht besetzt sein
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Wird standardmäßig im WS angeboten
        /// </summary>
        public bool InWS { get; set; }

        /// <summary>
        /// Wird standardmäßig im SS angeboten
        /// </summary>
        public bool InSS { get; set; }


        public virtual CurriculumGroup CurriculumGroup { get; set; }

        
        public virtual ICollection<GroupAlias> Aliases { get; set; }

        /// <summary>
        /// Liste der Semestergruppen
        /// </summary>
        public virtual ICollection<SemesterGroup> SemesterGroups { get; set; }


        public string FullName
        {
            get
            {
                if (CurriculumGroup != null && CurriculumGroup.Curriculum != null)
                {
                    if (string.IsNullOrEmpty(Name) && string.IsNullOrEmpty(CurriculumGroup.Name))
                        return CurriculumGroup.Curriculum.ShortName;

                    return $"{CurriculumGroup.Curriculum.ShortName} - {CurriculumGroup.Name} {Name}";
                }
                
                return "keine Zuordnung";
            }
        }

        public string GroupName
        {
            get
            {
                if (CurriculumGroup != null && CurriculumGroup.Curriculum != null)
                {
                    return string.Format("{0} {1}",
                        CurriculumGroup.Name,
                        Name);
                }

                return "keine Zuordnung";
            }
        }

    }
}
